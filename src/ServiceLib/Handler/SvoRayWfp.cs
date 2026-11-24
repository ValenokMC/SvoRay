namespace ServiceLib.Handler;

/// <summary>
/// The kill switch's filters in the Windows Filtering Platform, in a dynamic session of their own.
/// </summary>
/// <remarks>
/// WFP structures are written field by field at their offsets in the 64-bit layout, the way
/// <see cref="SvoRayForwarding"/> reads MIB_IPINTERFACE_ROW: the structures are full of unions and
/// pointers, and explicit offsets are easier to check against the SDK headers than marshalling
/// attributes. SvoRay ships for x64 only; in a 32-bit process the firewall refuses to open.
/// <para>
/// Every change is one WFP transaction: the old filters are deleted and the new ones added
/// together, so an update never leaves a moment without a block, and a failed one leaves the
/// previous filters in force.
/// </para>
/// </remarks>
public sealed class SvoRayWfp : IKillSwitchFirewall
{
    private const string _tag = "SvoRay WFP";

    private static readonly Guid SubLayerKey = new("8c7a1f0e-3b5d-4e2a-9f61-5d2b7c4e9a13");

    private static readonly Guid LayerAleAuthConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid LayerAleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid LayerAleAuthRecvAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    private static readonly Guid LayerAleAuthRecvAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");

    private static readonly Guid ConditionAleAppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    private static readonly Guid ConditionIpLocalInterface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
    private static readonly Guid ConditionFlags = new("632ce23b-5167-435c-86d7-e903684aa80c");
    private static readonly Guid ConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    private static readonly Guid ConditionIpLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
    private static readonly Guid ConditionIpRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    private static readonly Guid ConditionIpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");

    // FWP_DATA_TYPE
    private const uint FwpUint8 = 1;
    private const uint FwpUint16 = 2;
    private const uint FwpUint32 = 3;
    private const uint FwpUint64 = 4;
    private const uint FwpByteBlobType = 12;
    private const uint FwpV4AddrMask = 0x100;
    private const uint FwpV6AddrMask = 0x101;

    // FWP_MATCH_TYPE
    private const uint FwpMatchEqual = 0;
    private const uint FwpMatchFlagsAllSet = 6;

    private const uint FwpActionBlock = 0x1001;
    private const uint FwpActionPermit = 0x1002;

    private const uint FwpmSessionFlagDynamic = 0x1;
    private const uint FwpmFilterFlagClearActionRight = 0x8;
    private const uint FwpmFilterFlagDisabled = 0x20;
    private const uint FwpConditionFlagIsLoopback = 0x1;
    private const uint RpcCAuthnWinnt = 10;
    private const uint FwpEAlreadyExists = 0x80320009;

    // Sizes in the 64-bit layout.
    private const int SessionSize = 72;
    private const int SubLayerSize = 72;
    private const int FilterSize = 200;
    private const int ConditionSize = 40;

    private readonly bool _enforce;
    private readonly Lock _lock = new();
    private IntPtr _engine;
    private List<ulong> _filterIds = [];

    /// <param name="enforce">
    /// False adds every filter disabled: WFP checks and stores them but does not apply them. Used
    /// by the Windows tests, which must not cut the CI runner off its own network.
    /// </param>
    public SvoRayWfp(bool enforce = true)
    {
        _enforce = enforce;
    }

    public int FilterCount => _filterIds.Count;

    public bool Apply(IReadOnlyList<KillSwitchFilter> filters)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return false;
        }

        lock (_lock)
        {
            if (_engine == IntPtr.Zero && !Open())
            {
                return false;
            }

            using var memory = new NativeArena();
            var rc = FwpmTransactionBegin0(_engine, 0);
            if (rc != 0)
            {
                Logging.SaveLog($"{_tag}: transaction not started (0x{rc:X8})");
                return false;
            }

            var added = new List<ulong>();
            try
            {
                foreach (var id in _filterIds)
                {
                    rc = FwpmFilterDeleteById0(_engine, id);
                    if (rc != 0)
                    {
                        throw new InvalidOperationException($"filter {id} not deleted (0x{rc:X8})");
                    }
                }

                foreach (var filter in filters)
                {
                    var native = WriteFilter(filter, memory);
                    if (native == IntPtr.Zero)
                    {
                        // A program that cannot be named to WFP gets no exception: less passes, never more.
                        continue;
                    }
                    rc = FwpmFilterAdd0(_engine, native, IntPtr.Zero, out var id);
                    if (rc != 0)
                    {
                        throw new InvalidOperationException($"filter \"{filter.Name}\" on {filter.Layer} not added (0x{rc:X8})");
                    }
                    added.Add(id);
                }

                rc = FwpmTransactionCommit0(_engine);
                if (rc != 0)
                {
                    throw new InvalidOperationException($"transaction not committed (0x{rc:X8})");
                }
                _filterIds = added;
                return true;
            }
            catch (Exception ex)
            {
                _ = FwpmTransactionAbort0(_engine);
                Logging.SaveLog($"{_tag}: {ex.Message}");
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_engine != IntPtr.Zero && OperatingSystem.IsWindows())
            {
                // The session is dynamic: closing it deletes its sublayer and every filter in it.
                _ = FwpmEngineClose0(_engine);
            }
            _engine = IntPtr.Zero;
            _filterIds = [];
        }
    }

    /// <summary>The LUID of the adapter with that name (its alias), or null when there is none.</summary>
    public static ulong? FindInterfaceLuid(string alias)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            return ConvertInterfaceAliasToLuid(alias, out var luid) == 0 && luid != 0 ? luid : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private bool Open()
    {
        using var memory = new NativeArena();

        // FWPM_SESSION0: sessionKey 0, displayData 16 (name, description), flags 32,
        // txnWaitTimeoutInMSec 36, processId 40, sid 48, username 56, kernelMode 64.
        var session = memory.Alloc(SessionSize);
        Marshal.WriteIntPtr(session, 16, memory.String("SvoRay kill switch"));
        Marshal.WriteInt32(session, 32, unchecked((int)FwpmSessionFlagDynamic));

        var rc = FwpmEngineOpen0(null, RpcCAuthnWinnt, IntPtr.Zero, session, out var engine);
        if (rc != 0)
        {
            Logging.SaveLog($"{_tag}: engine not opened (0x{rc:X8})");
            return false;
        }

        // FWPM_SUBLAYER0: subLayerKey 0, displayData 16, flags 32, providerKey 40,
        // providerData 48, weight 64. The highest weight, so ours is looked at first.
        var subLayer = memory.Alloc(SubLayerSize);
        memory.WriteGuid(subLayer, 0, SubLayerKey);
        Marshal.WriteIntPtr(subLayer, 16, memory.String("SvoRay kill switch"));
        Marshal.WriteInt16(subLayer, 64, unchecked((short)0xFFFF));

        rc = FwpmSubLayerAdd0(engine, subLayer, IntPtr.Zero);
        if (rc != 0 && rc != FwpEAlreadyExists)
        {
            Logging.SaveLog($"{_tag}: sublayer not added (0x{rc:X8})");
            _ = FwpmEngineClose0(engine);
            return false;
        }

        _engine = engine;
        return true;
    }

    private IntPtr WriteFilter(KillSwitchFilter filter, NativeArena memory)
    {
        var conditions = IntPtr.Zero;
        if (filter.Conditions.Count > 0)
        {
            conditions = memory.Alloc(ConditionSize * filter.Conditions.Count);
            for (var i = 0; i < filter.Conditions.Count; i++)
            {
                if (!WriteCondition(filter.Conditions[i], conditions + (i * ConditionSize), memory))
                {
                    return IntPtr.Zero;
                }
            }
        }

        var flags = filter.Permit ? 0u : FwpmFilterFlagClearActionRight;
        if (!_enforce)
        {
            flags |= FwpmFilterFlagDisabled;
        }

        // FWPM_FILTER0: filterKey 0, displayData 16, flags 32, providerKey 40, providerData 48,
        // layerKey 64, subLayerKey 80, weight 96 (FWP_VALUE0: type, then the value at 104),
        // numFilterConditions 112, filterCondition 120, action 128 (type, then a GUID at 132),
        // rawContext 152, reserved 168, filterId 176, effectiveWeight 184.
        var native = memory.Alloc(FilterSize);
        Marshal.WriteIntPtr(native, 16, memory.String($"SvoRay kill switch: {filter.Name}"));
        Marshal.WriteInt32(native, 32, unchecked((int)flags));
        memory.WriteGuid(native, 64, LayerKey(filter.Layer));
        memory.WriteGuid(native, 80, SubLayerKey);
        Marshal.WriteInt32(native, 96, (int)FwpUint8);
        Marshal.WriteByte(native, 104, filter.Weight);
        Marshal.WriteInt32(native, 112, filter.Conditions.Count);
        Marshal.WriteIntPtr(native, 120, conditions);
        Marshal.WriteInt32(native, 128, unchecked((int)(filter.Permit ? FwpActionPermit : FwpActionBlock)));
        return native;
    }

    /// <summary>
    /// FWPM_FILTER_CONDITION0: fieldKey 0, matchType 16, conditionValue 24 (FWP_CONDITION_VALUE0:
    /// type, then the value or a pointer to it at 32).
    /// </summary>
    private static bool WriteCondition(KillSwitchCondition condition, IntPtr target, NativeArena memory)
    {
        Guid field;
        var match = FwpMatchEqual;
        uint type;
        switch (condition)
        {
            case KillSwitchCondition.Loopback:
                field = ConditionFlags;
                match = FwpMatchFlagsAllSet;
                type = FwpUint32;
                Marshal.WriteInt32(target, 32, (int)FwpConditionFlagIsLoopback);
                break;

            case KillSwitchCondition.Application app:
                if (FwpmGetAppIdFromFileName0(app.Path, out var blob) != 0 || blob == IntPtr.Zero)
                {
                    Logging.SaveLog($"{_tag}: no app id for {Path.GetFileName(app.Path)}");
                    return false;
                }
                memory.TrackWfp(blob);
                field = ConditionAleAppId;
                type = FwpByteBlobType;
                Marshal.WriteIntPtr(target, 32, blob);
                break;

            case KillSwitchCondition.Interface adapter:
                var luid = memory.Alloc(8);
                Marshal.WriteInt64(luid, 0, unchecked((long)adapter.Luid));
                field = ConditionIpLocalInterface;
                type = FwpUint64;
                Marshal.WriteIntPtr(target, 32, luid);
                break;

            case KillSwitchCondition.Protocol protocol:
                field = ConditionIpProtocol;
                type = FwpUint8;
                Marshal.WriteByte(target, 32, protocol.Number);
                break;

            case KillSwitchCondition.LocalPort port:
                field = ConditionIpLocalPort;
                type = FwpUint16;
                Marshal.WriteInt16(target, 32, unchecked((short)port.Port));
                break;

            case KillSwitchCondition.RemotePort port:
                field = ConditionIpRemotePort;
                type = FwpUint16;
                Marshal.WriteInt16(target, 32, unchecked((short)port.Port));
                break;

            case KillSwitchCondition.RemoteNetwork network when network.Address.AddressFamily == AddressFamily.InterNetwork:
            {
                // FWP_V4_ADDR_AND_MASK: addr, mask, both in host byte order.
                var bytes = network.Address.GetAddressBytes();
                var address = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
                var mask = network.PrefixLength == 0 ? 0u : uint.MaxValue << (32 - network.PrefixLength);
                var value = memory.Alloc(8);
                Marshal.WriteInt32(value, 0, unchecked((int)address));
                Marshal.WriteInt32(value, 4, unchecked((int)mask));
                field = ConditionIpRemoteAddress;
                type = FwpV4AddrMask;
                Marshal.WriteIntPtr(target, 32, value);
                break;
            }

            case KillSwitchCondition.RemoteNetwork network:
            {
                // FWP_V6_ADDR_AND_MASK: 16 address bytes in network order, then the prefix length.
                var value = memory.Alloc(17);
                Marshal.Copy(network.Address.GetAddressBytes(), 0, value, 16);
                Marshal.WriteByte(value, 16, network.PrefixLength);
                field = ConditionIpRemoteAddress;
                type = FwpV6AddrMask;
                Marshal.WriteIntPtr(target, 32, value);
                break;
            }

            default:
                return false;
        }

        memory.WriteGuid(target, 0, field);
        Marshal.WriteInt32(target, 16, (int)match);
        Marshal.WriteInt32(target, 24, (int)type);
        return true;
    }

    private static Guid LayerKey(KillSwitchLayer layer) => layer switch
    {
        KillSwitchLayer.ConnectV4 => LayerAleAuthConnectV4,
        KillSwitchLayer.ConnectV6 => LayerAleAuthConnectV6,
        KillSwitchLayer.AcceptV4 => LayerAleAuthRecvAcceptV4,
        _ => LayerAleAuthRecvAcceptV6,
    };

    /// <summary>Zeroed native memory for one call, freed together afterwards.</summary>
    private sealed class NativeArena : IDisposable
    {
        private readonly List<IntPtr> _blocks = [];
        private readonly List<IntPtr> _wfp = [];

        public IntPtr Alloc(int size)
        {
            var block = Marshal.AllocHGlobal(size);
            _blocks.Add(block);
            Marshal.Copy(new byte[size], 0, block, size);
            return block;
        }

        public IntPtr String(string value)
        {
            var block = Marshal.StringToHGlobalUni(value);
            _blocks.Add(block);
            return block;
        }

        public void WriteGuid(IntPtr target, int offset, Guid value)
        {
            Marshal.Copy(value.ToByteArray(), 0, target + offset, 16);
        }

        /// <summary>Memory WFP allocated, which only WFP may free.</summary>
        public void TrackWfp(IntPtr block) => _wfp.Add(block);

        public void Dispose()
        {
            foreach (var block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }
            if (OperatingSystem.IsWindows())
            {
                foreach (var block in _wfp)
                {
                    var pointer = block;
                    FwpmFreeMemory0(ref pointer);
                }
            }
            _blocks.Clear();
            _wfp.Clear();
        }
    }

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, IntPtr subLayer, IntPtr securityDescriptor);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterAdd0(IntPtr engineHandle, IntPtr filter, IntPtr securityDescriptor, out ulong id);

    [DllImport("fwpuclnt.dll")]
    private static extern uint FwpmFilterDeleteById0(IntPtr engineHandle, ulong id);

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    private static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

    [DllImport("fwpuclnt.dll")]
    private static extern void FwpmFreeMemory0(ref IntPtr p);

    [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint ConvertInterfaceAliasToLuid(string interfaceAlias, out ulong interfaceLuid);
}
