using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class DocsController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public DocsController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpGet("/Admin/Docs")]
        public IActionResult Index()
        {
            var docsRoot = Path.Combine(_env.ContentRootPath, "Docs");
            var tree = BuildTree(docsRoot, docsRoot);
            return View(tree);
        }

        [HttpGet("/Admin/Docs/View")]
        public IActionResult View(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return NotFound();

            // Sanitize: only allow safe relative paths (no .. traversal)
            var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                                 .Replace('\\', Path.DirectorySeparatorChar);
            if (normalized.Contains(".."))
                return BadRequest();

            var docsRoot = Path.Combine(_env.ContentRootPath, "Docs");
            var fullPath = Path.GetFullPath(Path.Combine(docsRoot, normalized));

            if (!fullPath.StartsWith(docsRoot, StringComparison.OrdinalIgnoreCase))
                return BadRequest();

            if (!System.IO.File.Exists(fullPath) || !fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return NotFound();

            var content = System.IO.File.ReadAllText(fullPath);
            ViewBag.RelativePath = path;
            ViewBag.Title = Path.GetFileNameWithoutExtension(fullPath);
            return View((object)content);
        }

        private DocNode BuildTree(string docsRoot, string dir)
        {
            var node = new DocNode
            {
                Name = Path.GetFileName(dir) == "Docs" ? "Documentation" : Path.GetFileName(dir),
                IsDirectory = true
            };

            foreach (var subDir in Directory.GetDirectories(dir).OrderBy(d => d))
            {
                node.Children.Add(BuildTree(docsRoot, subDir));
            }

            foreach (var file in Directory.GetFiles(dir, "*.md").OrderBy(f => f))
            {
                var rel = Path.GetRelativePath(docsRoot, file).Replace('\\', '/');
                node.Children.Add(new DocNode
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    RelativePath = rel,
                    IsDirectory = false
                });
            }

            return node;
        }
    }

    public class DocNode
    {
        public string Name { get; set; } = string.Empty;
        public string? RelativePath { get; set; }
        public bool IsDirectory { get; set; }
        public List<DocNode> Children { get; set; } = new();
    }
}
