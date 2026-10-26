namespace ServiceLib.Enums;

/// <summary>What a row of the simple screen's server list is.</summary>
public enum ESimpleServerItemKind
{
    Server,
    /// <summary>The heading of one subscription's servers; removes the whole subscription.</summary>
    Subscription,
    /// <summary>The heading of the servers added on their own.</summary>
    Standalone,
    /// <summary>The last row, which opens the import form.</summary>
    Add,
}
