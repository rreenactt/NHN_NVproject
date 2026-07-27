using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace NV.Architecture.Tests
{
    public class ModuleBoundaryTests
    {
        private const string ModulesTestAssemblyName = "Modules.Tests";

        [Fact]
        public void 모듈은_서로를_참조하지_않는다()
        {
            var moduleNames = ModuleNames();
            var violations = new List<string>();

            foreach (var module in SolutionLayout.Modules)
            {
                foreach (var reference in module.ReferencedProjects)
                {
                    if (moduleNames.Contains(reference) &&
                        !string.Equals(reference, module.Name, StringComparison.Ordinal))
                    {
                        violations.Add($"{module.Name} -> {reference}");
                    }
                }
            }

            Assert.True(violations.Count == 0, $"모듈 간 참조 발견: {string.Join(", ", violations)}");
        }

        [Fact]
        public void Infrastructure는_모듈을_참조하지_않는다()
        {
            var moduleNames = ModuleNames();
            var infrastructure = SolutionLayout.Project("Infrastructure");

            var violations = infrastructure.ReferencedProjects
                .Where(moduleNames.Contains)
                .ToArray();

            Assert.True(
                violations.Length == 0,
                $"Infrastructure 가 모듈을 참조한다: {string.Join(", ", violations)}");
        }

        [Fact]
        public void 모듈의_공개_타입은_Contracts와_Module_클래스뿐이다()
        {
            var violations = new List<string>();

            foreach (var module in RequireModules())
            {
                var contractsNamespace = $"NV.{module.Name}.Contracts";
                var moduleClassName = $"{module.Name}Module";

                foreach (var type in SolutionLayout.LoadAssembly(module).GetExportedTypes())
                {
                    var typeNamespace = type.Namespace ?? string.Empty;

                    var inContracts =
                        string.Equals(typeNamespace, contractsNamespace, StringComparison.Ordinal) ||
                        typeNamespace.StartsWith(contractsNamespace + ".", StringComparison.Ordinal);

                    var isModuleClass = string.Equals(type.Name, moduleClassName, StringComparison.Ordinal);

                    if (!inContracts && !isModuleClass)
                    {
                        violations.Add($"{module.Name}: {type.FullName}");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Contracts 밖의 public 타입 발견: {string.Join(", ", violations)}");
        }

        [Fact]
        public void 모듈은_Modules_Tests에_internal을_공개한다()
        {
            var violations = new List<string>();

            foreach (var module in RequireModules())
            {
                var opened = SolutionLayout.LoadAssembly(module)
                    .GetCustomAttributes<InternalsVisibleToAttribute>()
                    .Any(attribute => attribute.AssemblyName.StartsWith(ModulesTestAssemblyName, StringComparison.Ordinal));

                if (!opened)
                {
                    violations.Add(module.Name);
                }
            }

            Assert.True(
                violations.Count == 0,
                $"AssemblyInfo 에 InternalsVisibleTo(\"{ModulesTestAssemblyName}\") 가 없다: {string.Join(", ", violations)}");
        }

        private static HashSet<string> ModuleNames()
        {
            return RequireModules()
                .Select(module => module.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// 모듈이 하나도 없으면 위 검사들이 전부 공허하게 통과한다.
        private static IReadOnlyList<ProjectInfo> RequireModules()
        {
            var modules = SolutionLayout.Modules;
            Assert.True(modules.Count > 0, $"Modules/ 아래에서 프로젝트를 찾지 못했다. 탐색 기준: {SolutionLayout.RepositoryRoot}");
            return modules;
        }
    }
}
