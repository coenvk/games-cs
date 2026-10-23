using System.Security.Cryptography;
using System.Text;

namespace Sokoban.Model.Tests;

/// <summary>
/// Saved progress is keyed by <see cref="Level.Key"/>, so keys must never change: if they did,
/// every player's progress would silently disappear. These values were taken from the shipped game.
/// </summary>
public class KeyStabilityTests
{
    [Fact]
    public void ClassicLevelOne_KeepsTheKeyStoredInPlayersSaves() =>
        Pack("original.xsb")[0].Key.Should().Be("dbcbe280ef3f1c18");

    [Fact]
    public void AllShippedLevels_KeepTheirKeys()
    {
        var keys = string.Join('\n', Pack("original.xsb").Concat(Pack("ice.xsb")).Select(l => l.Key));
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(keys)))[..16];

        fingerprint.Should().Be("435356fe82636245");
    }

    private static IReadOnlyList<Level> Pack(string file) =>
        Xsb.ParsePack(File.ReadAllText(Path.Combine("levels", file)));
}
