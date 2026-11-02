using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Tests;

[Collection("Sequential")]
public sealed class WorkspaceAppConfigTests
{
    [Fact]
    public void Initialize_RoleScrape_ConfiguresCorrectly()
    {
        WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);

        Assert.Equal(WorkspaceRole.Scrape, WorkspaceAppConfig.Role);
        Assert.True(WorkspaceAppConfig.ShowScrape);
        Assert.False(WorkspaceAppConfig.ShowImportUpdate);
        Assert.True(WorkspaceAppConfig.IsScrapeOnly);
        Assert.False(WorkspaceAppConfig.IsImportUpdateOnly);
        Assert.Equal("Shopee Scrape", WorkspaceAppConfig.WindowTitle);
        Assert.EndsWith("/Assets/scrape-icon.ico", WorkspaceAppConfig.WindowIcon);
    }

    [Fact]
    public void Initialize_RoleImportUpdate_ConfiguresCorrectly()
    {
        WorkspaceAppConfig.Initialize(WorkspaceRole.ImportUpdate);

        Assert.Equal(WorkspaceRole.ImportUpdate, WorkspaceAppConfig.Role);
        Assert.False(WorkspaceAppConfig.ShowScrape);
        Assert.True(WorkspaceAppConfig.ShowImportUpdate);
        Assert.False(WorkspaceAppConfig.IsScrapeOnly);
        Assert.True(WorkspaceAppConfig.IsImportUpdateOnly);
        Assert.Equal("Shopee Import & Update", WorkspaceAppConfig.WindowTitle);
        Assert.EndsWith("/Assets/update-icon.ico", WorkspaceAppConfig.WindowIcon);
    }

    [Fact]
    public void Initialize_RoleFull_ConfiguresCorrectly()
    {
        WorkspaceAppConfig.Initialize(WorkspaceRole.Full);

        Assert.Equal(WorkspaceRole.Full, WorkspaceAppConfig.Role);
        Assert.True(WorkspaceAppConfig.ShowScrape);
        Assert.True(WorkspaceAppConfig.ShowImportUpdate);
        Assert.False(WorkspaceAppConfig.IsScrapeOnly);
        Assert.False(WorkspaceAppConfig.IsImportUpdateOnly);
        Assert.Equal("Shopee Workspace", WorkspaceAppConfig.WindowTitle);
        Assert.EndsWith("/Assets/app-icon.ico", WorkspaceAppConfig.WindowIcon);
    }

    [Theory]
    [InlineData(new string[] { "--scrape" }, WorkspaceRole.Scrape)]
    [InlineData(new string[] { "--update" }, WorkspaceRole.ImportUpdate)]
    [InlineData(new string[] { "--import-update" }, WorkspaceRole.ImportUpdate)]
    [InlineData(new string[] { "--role", "scrape" }, WorkspaceRole.Scrape)]
    [InlineData(new string[] { "--role", "update" }, WorkspaceRole.ImportUpdate)]
    [InlineData(new string[] { "--role", "import-update" }, WorkspaceRole.ImportUpdate)]
    [InlineData(new string[] { }, WorkspaceRole.Full)]
    public void InitializeFromArgs_ParsesExpectedRole(string[] args, WorkspaceRole expectedRole)
    {
        WorkspaceAppConfig.InitializeFromArgs(args);
        Assert.Equal(expectedRole, WorkspaceAppConfig.Role);
    }

    [Fact]
    public void InitializeFromArgs_NullArgs_DefaultsToFull()
    {
        WorkspaceAppConfig.InitializeFromArgs(null);
        Assert.Equal(WorkspaceRole.Full, WorkspaceAppConfig.Role);
    }
}
