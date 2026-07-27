using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace NV.Architecture.Tests
{
    /// 프로젝트 그래프를 .csproj 선언에서 읽는다.
    /// IL 참조(Assembly.GetReferencedAssemblies)는 실제로 타입을 쓰지 않으면 나타나지 않는다.
    /// 선언만 하고 아직 쓰지 않는 참조도 규칙 위반이므로 .csproj 를 출처로 삼는다.
    internal static class SolutionLayout
    {
        private const string SolutionFileName = "NVserver.slnx";
        private const string ModulesFolderName = "Modules";

        private static readonly string[] ExcludedRoots = { "artifacts", "Client", "node_modules" };

        public static string RepositoryRoot { get; } = FindRepositoryRoot();

        public static IReadOnlyList<ProjectInfo> Projects { get; } = LoadProjects();

        public static IReadOnlyList<ProjectInfo> Modules { get; } =
            Projects.Where(project => project.IsModule).ToArray();

        public static ProjectInfo Project(string name)
        {
            return Projects.Single(project => string.Equals(project.Name, name, StringComparison.Ordinal));
        }

        public static Assembly LoadAssembly(ProjectInfo project)
        {
            var path = Path.Combine(AppContext.BaseDirectory, project.Name + ".dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"{project.Name} 어셈블리가 출력 폴더에 없다. Architecture.Tests.csproj 에 ProjectReference 를 추가한다.",
                    path);
            }

            return Assembly.LoadFrom(path);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException($"{SolutionFileName} 을 상위 경로에서 찾지 못했다.");
            }

            return directory.FullName;
        }

        private static IReadOnlyList<ProjectInfo> LoadProjects()
        {
            var root = RepositoryRoot;
            var projects = new List<ProjectInfo>();

            foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            {
                var segments = Path.GetRelativePath(root, file)
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });

                if (ExcludedRoots.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                projects.Add(new ProjectInfo(
                    Path.GetFileNameWithoutExtension(file),
                    file,
                    string.Equals(segments[0], ModulesFolderName, StringComparison.Ordinal),
                    ReadProjectReferences(file)));
            }

            return projects;
        }

        private static IReadOnlyCollection<string> ReadProjectReferences(string projectFile)
        {
            return XDocument.Load(projectFile)
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
                .ToArray();
        }
    }

    internal sealed class ProjectInfo
    {
        public ProjectInfo(string name, string fullPath, bool isModule, IReadOnlyCollection<string> referencedProjects)
        {
            Name = name;
            FullPath = fullPath;
            IsModule = isModule;
            ReferencedProjects = referencedProjects;
        }

        public string Name { get; }

        public string FullPath { get; }

        public bool IsModule { get; }

        public IReadOnlyCollection<string> ReferencedProjects { get; }
    }
}
