using static ServiceLib.Models.Dto.ClashProxies;

namespace ServiceLib.Manager;

public sealed class ClashApiManager
{
    private static readonly Lazy<ClashApiManager> instance = new(() => new());
    public static ClashApiManager Instance => instance.Value;

    private static readonly string _tag = "ClashApiHandler";
    private Dictionary<string, ProxiesItem>? _proxies;
    public Dictionary<string, object> ProfileContent { get; set; }

    public async Task<Tuple<ClashProxies, ClashProviders>?> GetClashProxiesAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            var url = $"{GetApiUrl()}/proxies";
            var result = await HttpClientHelper.Instance.TryGetAsync(url);
            var clashProxies = JsonUtils.Deserialize<ClashProxies>(result);

            var url2 = $"{GetApiUrl()}/providers/proxies";
            var result2 = await HttpClientHelper.Instance.TryGetAsync(url2);
            var clashProviders = JsonUtils.Deserialize<ClashProviders>(result2);

            if (clashProxies != null || clashProviders != null)
            {
                _proxies = clashProxies?.proxies;
                return new Tuple<ClashProxies, ClashProviders>(clashProxies, clashProviders);
            }

            await Task.Delay(2000);
        }

        return null;
    }

    public void ClashProxiesDelayTest(bool blAll, List<ClashProxyModel> lstProxy, Func<ClashProxyModel?, string, Task> updateFunc)
    {
        Task.Run(async () =>
        {
            if (blAll)
            {
                if (_proxies == null)
                {
                    await GetClashProxiesAsync();
                }
                lstProxy = [];
                lstProxy.AddRange(from kv in _proxies ?? []
                                  where !Global.notAllowTestType.Contains(kv.Value.type?.ToLower())
                                  select new ClashProxyModel()
                                  {
                                      Name = kv.Value.name,
                                      Type = kv.Value.type?.ToLower(),
                                  });
            }

            if (lstProxy is not { Count: > 0 })
            {
                return;
            }
            var urlBase = $"{GetApiUrl()}/proxies";
            urlBase += @"/{0}/delay?timeout=10000&url=" + AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl;

            var tasks = new List<Task>();
            foreach (var it in lstProxy)
            {
                if (Global.notAllowTestType.Contains(it.Type.ToLower()))
                {
                    continue;
                }
                var name = it.Name;
                var url = string.Format(urlBase, name);
                tasks.Add(Task.Run(async () =>
                {
                    var result = await HttpClientHelper.Instance.TryGetAsync(url);
                    await updateFunc?.Invoke(it, result);
                }));
            }
            await Task.WhenAll(tasks);
            await Task.Delay(1000);
            await updateFunc?.Invoke(null, "");
        });
    }

    public List<ProxiesItem>? GetClashProxyGroups()
    {
        try
        {
            var fileContent = ProfileContent;
            if (fileContent is null || fileContent?.ContainsKey("proxy-groups") == false)
            {
                return null;
            }
            return JsonUtils.Deserialize<List<ProxiesItem>>(JsonUtils.Serialize(fileContent["proxy-groups"]));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    public async Task ClashSetActiveProxy(string name, string nameNode)
    {
        try
        {
            var url = $"{GetApiUrl()}/proxies/{name}";
            var headers = new Dictionary<string, string>();
            headers.Add("name", nameNode);
            await HttpClientHelper.Instance.PutAsync(url, headers);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Points the selector <paramref name="selector"/> of the running core at
    /// <paramref name="outboundTag"/> and reports whether the core accepted it: a server switch
    /// that relies on it must fall back to a restart otherwise.
    /// </summary>
    public async Task<bool> TrySelectProxyAsync(string selector, string outboundTag, int timeoutMs = 2000)
    {
        try
        {
            var url = $"{GetApiUrl()}/proxies/{Uri.EscapeDataString(selector)}";
            var body = new Dictionary<string, string> { ["name"] = outboundTag };
            return await HttpClientHelper.Instance.TryPutAsync(url, body, TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
    }

    public async Task ClashConfigUpdate(Dictionary<string, string> headers)
    {
        if (_proxies == null)
        {
            return;
        }

        var urlBase = $"{GetApiUrl()}/configs";

        await HttpClientHelper.Instance.PatchAsync(urlBase, headers);
    }

    public async Task ClashConfigReload(string filePath)
    {
        await ClashConnectionClose("");
        try
        {
            var url = $"{GetApiUrl()}/configs?force=true";
            var headers = new Dictionary<string, string>();
            headers.Add("path", filePath);
            await HttpClientHelper.Instance.PutAsync(url, headers);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public async Task<ClashConnections?> GetClashConnectionsAsync()
    {
        try
        {
            var url = $"{GetApiUrl()}/connections";
            var result = await HttpClientHelper.Instance.TryGetAsync(url);
            var clashConnections = JsonUtils.Deserialize<ClashConnections>(result);

            return clashConnections;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }

        return null;
    }

    public async Task ClashConnectionClose(string id)
    {
        try
        {
            var url = $"{GetApiUrl()}/connections/{id}";
            await HttpClientHelper.Instance.DeleteAsync(url);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private string GetApiUrl()
    {
        return $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort2}";
    }
}
