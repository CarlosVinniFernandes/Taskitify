using Taskitify.Services;

namespace Taskitify.Tests;

public class LatestVersionPolicyTests
{
    private const string Here = @"C:\Apps\Taskitify-1.0.5\Taskitify.exe";
    private static readonly Version This = new(1, 0, 5, 0);

    private static bool Decide(string? current, Version? currentVersion) =>
        LatestVersionPolicy.ShouldPointHere(current, Here, This, _ => currentVersion);

    [Fact]
    public void CreatesWhenThereIsNone() => Assert.True(Decide(null, null));

    [Fact]
    public void LeavesTheStoreAppAlone() => Assert.False(Decide("", null));

    [Fact]
    public void NothingToDoWhenItAlreadyPointsHere() => Assert.False(Decide(@"c:\apps\taskitify-1.0.5\TASKITIFY.EXE", This));

    [Fact]
    public void MovesFromAnOlderCopy() => Assert.True(Decide(@"C:\Apps\Taskitify-1.0.4\Taskitify.exe", new Version(1, 0, 4, 0)));

    [Fact]
    public void MovesFromACopyOfTheSameVersion() => Assert.True(Decide(@"D:\Taskitify.exe", This));

    [Fact]
    public void MovesFromACopyThatIsGone() => Assert.True(Decide(@"C:\Old\Taskitify.exe", null));

    [Fact]
    public void KeepsANewerCopy() => Assert.False(Decide(@"C:\Apps\Taskitify-1.1.0\Taskitify.exe", new Version(1, 1, 0, 0)));
}
