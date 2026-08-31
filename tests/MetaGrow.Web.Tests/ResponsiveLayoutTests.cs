namespace MetaGrow.Web.Tests;

public class ResponsiveLayoutTests
{
    [Fact]
    public void Main_layout_defines_named_page_content_container()
    {
        var repositoryRoot = FindRepositoryRoot();
        var layoutCss = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Layout",
            "MainLayout.razor.css"));

        Assert.Contains("container-name: page-content;", layoutCss);
        Assert.Contains("container-type: inline-size;", layoutCss);
    }

    [Fact]
    public void Main_layout_links_authenticated_user_to_account_management()
    {
        var repositoryRoot = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Layout",
            "MainLayout.razor"));

        Assert.Contains("@AddDrawerStateToUrl(\"/Account/Manage/EmailAddresses\")", layout);
        Assert.Contains("@auth.User.Identity?.Name</NavLink>", layout);
    }

    [Fact]
    public void Page_width_breakpoints_use_page_content_container()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagesDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Pages");
        var pageCssFiles = Directory.GetFiles(pagesDirectory, "*.razor.css", SearchOption.AllDirectories);

        Assert.NotEmpty(pageCssFiles);

        var viewportWidthBreakpoints = pageCssFiles
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, index }))
            .Where(entry => entry.line.Contains("@media", StringComparison.Ordinal)
                && entry.line.Contains("max-width", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(repositoryRoot, entry.path)}:{entry.index + 1}")
            .ToArray();

        Assert.True(
            viewportWidthBreakpoints.Length == 0,
            $"Page layout breakpoints must query the page-content container: {string.Join(", ", viewportWidthBreakpoints)}");
        Assert.Contains(
            pageCssFiles,
            path => File.ReadAllText(path).Contains("@container page-content", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetaGrow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the MetaGrow repository root.");
    }
}
