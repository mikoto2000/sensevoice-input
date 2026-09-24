using System.IO;
using System.Xml.Linq;
using SenseVoiceInput.Windows;
using Windows.ApplicationModel;

namespace SenseVoiceInput.Windows.Tests;

public sealed class PackagedLoginStartupTests
{
    [Theory]
    [InlineData(StartupTaskState.Disabled, false, true)]
    [InlineData(StartupTaskState.Enabled, true, true)]
    [InlineData(StartupTaskState.DisabledByUser, false, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false, false)]
    [InlineData(StartupTaskState.EnabledByPolicy, true, false)]
    public async Task ReadsOsStateWithoutChangingIt(StartupTaskState state, bool enabled, bool canChange)
    {
        var task = new FakeTask(state);
        var result = await new PackagedLoginStartupService(task).GetStatusAsync();
        Assert.Equal(enabled, result.Enabled);
        Assert.Equal(canChange, result.CanChange);
        Assert.NotEmpty(result.Message);
        Assert.Equal(0, task.EnableCalls + task.DisableCalls);
    }

    [Theory]
    [InlineData(StartupTaskState.DisabledByUser, true)]
    [InlineData(StartupTaskState.DisabledByPolicy, true)]
    [InlineData(StartupTaskState.EnabledByPolicy, false)]
    public async Task DoesNotOverrideWindowsOrPolicy(StartupTaskState state, bool requested)
    {
        var task = new FakeTask(state);
        bool saved = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PackagedLoginStartupService(task).SaveAsync(requested, () => saved = true));
        Assert.False(saved);
        Assert.Equal(0, task.EnableCalls + task.DisableCalls);
    }

    [Theory]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false)]
    [InlineData(StartupTaskState.EnabledByPolicy, true)]
    public async Task OtherSettingsCanBeSavedWithoutChangingBlockedStartup(StartupTaskState state, bool enabled)
    {
        var task = new FakeTask(state);
        bool saved = false;
        await new PackagedLoginStartupService(task).SaveAsync(enabled, () => saved = true);
        Assert.True(saved);
        Assert.Equal(0, task.EnableCalls + task.DisableCalls);
    }

    [Fact]
    public async Task CanEnableAndDisableAndSaveAfterWindowsConfirms()
    {
        var task = new FakeTask(StartupTaskState.Disabled);
        var service = new PackagedLoginStartupService(task);
        await service.SaveAsync(true, () => Assert.Equal(StartupTaskState.Enabled, task.State));
        await service.SaveAsync(false, () => Assert.Equal(StartupTaskState.Disabled, task.State));
        Assert.Equal(1, task.EnableCalls);
        Assert.Equal(1, task.DisableCalls);
    }

    [Fact]
    public async Task DeniedEnableDoesNotSaveSettings()
    {
        var task = new FakeTask(StartupTaskState.Disabled) { EnableResult = StartupTaskState.DisabledByUser };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PackagedLoginStartupService(task).SaveAsync(true, () => Assert.Fail("Must not save")));
        Assert.Equal(StartupTaskState.DisabledByUser, task.State);
    }

    [Theory]
    [InlineData(StartupTaskState.Disabled, true)]
    [InlineData(StartupTaskState.Enabled, false)]
    public async Task FailedSettingsWriteRestoresPreviousState(StartupTaskState state, bool requested)
    {
        var task = new FakeTask(state);
        await Assert.ThrowsAsync<IOException>(() => new PackagedLoginStartupService(task).SaveAsync(requested, () => throw new IOException("disk full")));
        Assert.Equal(state, task.State);
    }

    [Fact]
    public async Task RollbackDoesNotOverrideAnExternalUserDisable()
    {
        var task = new FakeTask(StartupTaskState.Disabled);
        await Assert.ThrowsAsync<IOException>(() => new PackagedLoginStartupService(task).SaveAsync(true, () =>
        {
            task.State = StartupTaskState.DisabledByUser;
            throw new IOException("disk full");
        }));
        Assert.Equal(StartupTaskState.DisabledByUser, task.State);
        Assert.Equal(1, task.EnableCalls);
        Assert.Equal(0, task.DisableCalls);
    }

    [Fact]
    public async Task RollbackFailureReportsBothErrors()
    {
        var task = new FakeTask(StartupTaskState.Enabled) { EnableResult = StartupTaskState.DisabledByPolicy };
        var error = await Assert.ThrowsAsync<AggregateException>(() => new PackagedLoginStartupService(task).SaveAsync(false, () => throw new IOException("disk full")));
        Assert.IsType<IOException>(error.InnerExceptions[0]);
        Assert.Equal(StartupTaskState.DisabledByPolicy, task.State);
    }

    [Fact]
    public void UnpackagedProcessUsesRegistryImplementation() => Assert.IsType<LoginStartupService>(LoginStartupFactory.Create("unused.exe"));

    [Fact]
    public void ManifestUsesSameTaskIdAndStartsDisabledWithStartupArgument()
    {
        using var stream = typeof(PackagedLoginStartupTests).Assembly.GetManifestResourceStream("StartupManifest")!;
        var xml = XDocument.Load(stream);
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        XNamespace uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
        var task = Assert.Single(xml.Descendants(desktop + "StartupTask"));
        Assert.Equal(WindowsStartupTask.TaskId, (string?)task.Attribute("TaskId"));
        Assert.Equal("false", (string?)task.Attribute("Enabled"));
        Assert.Equal("--startup", (string?)task.Parent!.Attribute(uap10 + "Parameters"));
    }

    private sealed class FakeTask(StartupTaskState state) : IStartupTaskControl
    {
        public StartupTaskState State { get; set; } = state;
        public StartupTaskState EnableResult { get; init; } = StartupTaskState.Enabled;
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public Task<StartupTaskState> GetStateAsync() => Task.FromResult(State);
        public Task<StartupTaskState> RequestEnableAsync() { EnableCalls++; State = EnableResult; return Task.FromResult(State); }
        public Task DisableAsync() { DisableCalls++; State = StartupTaskState.Disabled; return Task.CompletedTask; }
    }
}
