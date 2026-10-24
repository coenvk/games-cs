using CodeLock;
using Jentl.Core.Storage;

namespace Games;

/// <summary>Owns the code lock records and saves them after every finished game.</summary>
internal sealed class CodeLockStore
{
    private readonly SavedFile<LockRecords> _file;

    /// <param name="store">Where to save, or null to keep the records in memory only.</param>
    public CodeLockStore(ISaveStore? store) =>
        _file = SavedFile<LockRecords>.Load(store, "codelock.json", LockRecords.FromJson, r => r.ToJson(), () => new LockRecords());

    public LockRecords Records => _file.Value;

    public LockResult Record(string difficulty, bool solved, int tries)
    {
        var result = Records.Record(difficulty, solved, tries);
        _file.Save();
        return result;
    }
}
