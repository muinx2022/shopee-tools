using System;
using System.Threading;
using System.Windows;
using Shopee.Workspace.Infrastructure;
using Shopee.Workspace.Modules.Workspace;
using Shopee.Workspace.ViewModels;
using Xunit;

namespace Shopee.Workspace.Tests;

public sealed class ScrapeMultiViewTests
{
    [Fact]
    public void RenderScrapeMultiView_LoadsAndMeasuresWithoutExceptions()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                WorkspaceAppConfig.Initialize(WorkspaceRole.Scrape);
                if (Application.Current is null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var shell = new ShellViewModel();
                var view = new ScrapeMultiView();
                view.DataContext = shell.Workspace.ScrapeMulti;
                shell.Workspace.ScrapeMulti.RefreshAccounts();
                view.Measure(new Size(1500, 1000));
                view.Arrange(new Rect(0, 0, 1500, 1000));
                view.UpdateLayout();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new AggregateException("ScrapeMultiView failed to render!", error);
        }
    }
}
