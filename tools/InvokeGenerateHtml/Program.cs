using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            Run().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static async Task Run()
    {
        // Locate the compiled Functions assembly by searching upward from the current working directory.
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        string? assemblyPath = null;
        for (int depth = 0; depth < 8 && current != null; depth++)
        {
            try
            {
                var found = Directory.EnumerateFiles(current.FullName, "ProcessedPostsApi.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(found))
                {
                    assemblyPath = found;
                    break;
                }
            }
            catch { }
            current = current.Parent;
        }
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Could not find ProcessedPostsApi.dll. Build the functions project first and run this from within the repo.");
        }
        // Prefer resolving dependent assemblies from the same folder as the found Functions assembly.
        var asmFolder = Path.GetDirectoryName(assemblyPath)!;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            try
            {
                var candidate = Path.Combine(asmFolder, name.Name + ".dll");
                if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
            }
            catch { }
            return null;
        };

        var asm = Assembly.LoadFrom(assemblyPath);
        var genType = asm.GetType("ProcessedPostsApi.Functions.GenerateEmailHtml") ?? throw new Exception("Type not found: GenerateEmailHtml");
        var blogPostType = asm.GetType("ProcessedPostsApi.Functions.BlogPost") ?? throw new Exception("Type not found: BlogPost");

        // Create a BlogPost instance and populate fields similar to the problematic post
        var post = Activator.CreateInstance(blogPostType)!;
        blogPostType.GetProperty("Title")!.SetValue(post, "New Microsoft e-book: 3 reasons point solutions are holding you back");
        blogPostType.GetProperty("Link")!.SetValue(post, "https://www.microsoft.com/en-us/security/blog/2025/12/18/new-microsoft-e-book-3-reasons-point-solutions-are-holding-you-back/");
        blogPostType.GetProperty("PublishDate")!.SetValue(post, "2025-12-18T00:00:00Z");
        blogPostType.GetProperty("Summary")!.SetValue(post, "Explore the new Microsoft e-book on how a unified, AI-ready platform delivers speed, resilience, and measurable security gains. The post New Microsoft e-book: 3 reasons point solutions are holding you back appeared first on Microsoft Security Blog.");
        blogPostType.GetProperty("SourceName")!.SetValue(post, "SecurityBlog");
        // Simulate EnglishSummary returned by SummarizePost
        var enSummary = new string[] {
            "While patchwork tools slow defenders down and impact visibility into potential cyberthreats, they’re an unfortunate reality for many organizations.",
            "As digital risk accelerates and attack surfaces multiply, security leaders are doing their best to stitch together point solutions while trying to avoid blind spots that cyberattackers can exploit.",
            "But point solutions can only go so far."
        };
        blogPostType.GetProperty("EnglishSummary")!.SetValue(post, enSummary);

        var postsArray = Array.CreateInstance(blogPostType, 1);
        postsArray.SetValue(post, 0);

        // Create a NullLogger via Microsoft.Extensions.Logging.Abstractions
        var loggerFactoryType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory);
        var nullLoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var createLoggerMethod = typeof(Microsoft.Extensions.Logging.ILoggerFactory).GetMethod("CreateLogger", new Type[] { typeof(string) });
        var logger = nullLoggerFactory.CreateLogger(genType.FullName!);

        // Instantiate GenerateEmailHtml
        var instance = Activator.CreateInstance(genType, logger) ?? throw new Exception("Failed to create GenerateEmailHtml instance");

        // Find the private GenerateHtmlAsync method
        var method = genType.GetMethod("GenerateHtmlAsync", BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new Exception("GenerateHtmlAsync not found");

        // Invoke GenerateHtmlAsync(posts, hasNewPosts, newPostsCount, recentPostsCount)
        var task = (Task)method.Invoke(instance, new object[] { postsArray, false, 0, 1 })!;
        await task.ConfigureAwait(false);

        // get Result property
        var resultProperty = task.GetType().GetProperty("Result");
        var html = resultProperty!.GetValue(task) as string ?? string.Empty;

        var outPath = Path.GetFullPath(Path.Combine("..", "..", "..", ".artifacts", "invoke_test_output.html"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, html);
        Console.WriteLine($"Saved generated HTML to {outPath}");
    }
}
