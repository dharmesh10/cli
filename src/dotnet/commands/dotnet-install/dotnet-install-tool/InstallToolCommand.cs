// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.ShellShim;
using Microsoft.DotNet.ToolPackage;
using Microsoft.Extensions.EnvironmentAbstractions;

namespace Microsoft.DotNet.Tools.Install.Tool
{
    public class InstallToolCommand : CommandBase
    {
        private static string _packageId;
        private static string _packageVersion;
        private static string _configFilePath;
        private static string _framework;

        public InstallToolCommand(
            AppliedOption appliedCommand,
            ParseResult parseResult)
            : base(parseResult)
        {
            if (appliedCommand == null)
            {
                throw new ArgumentNullException(nameof(appliedCommand));
            }

            _packageId = appliedCommand.Arguments.Single();
            _packageVersion = appliedCommand.ValueOrDefault<string>("version");
            _configFilePath = appliedCommand.ValueOrDefault<string>("configfile");
            _framework = appliedCommand.ValueOrDefault<string>("framework");
        }

        public override int Execute()
        {
            FilePath? configFile = null;

            if (_configFilePath != null)
            {
                configFile = new FilePath(_configFilePath);
            }

            var executablePackagePath = new DirectoryPath(new CliFolderPathCalculator().ExecutablePackagesPath);

            var toolConfigurationAndExecutableDirectory = ObtainPackage(
                _packageId,
                _packageVersion,
                configFile,
                _framework,
                executablePackagePath);

            DirectoryPath executable = toolConfigurationAndExecutableDirectory
                .ExecutableDirectory
                .WithSubDirectories(
                    toolConfigurationAndExecutableDirectory
                        .Configuration
                        .ToolAssemblyEntryPoint);

            var shellShimMaker = new ShellShimMaker(executablePackagePath.Value);
            var commandName = toolConfigurationAndExecutableDirectory.Configuration.CommandName;
            shellShimMaker.EnsureCommandNameUniqueness(commandName);

            shellShimMaker.CreateShim(
                executable.Value,
                commandName);

            EnvironmentPathFactory
                .CreateEnvironmentPathInstruction()
                .PrintAddPathInstructionIfPathDoesNotExist();

            Reporter.Output.WriteLine(
                $"{Environment.NewLine}The installation succeeded. If there is no other instruction. You can type the following command in shell directly to invoke: {commandName}");

            return 0;
        }

        private static ToolConfigurationAndExecutableDirectory ObtainPackage(
            string packageId,
            string packageVersion,
            FilePath? configFile,
            string framework,
            DirectoryPath executablePackagePath)
        {
            try
            {
                var toolPackageObtainer =
                    new ToolPackageObtainer(
                        executablePackagePath,
                        () => new DirectoryPath(Path.GetTempPath())
                            .WithSubDirectories(Path.GetRandomFileName())
                            .WithFile(Path.GetRandomFileName() + ".csproj"),
                        new Lazy<string>(BundledTargetFramework.GetTargetFrameworkMoniker),
                        new PackageToProjectFileAdder(),
                        new ProjectRestorer());

                return toolPackageObtainer.ObtainAndReturnExecutablePath(
                    packageId: packageId,
                    packageVersion: packageVersion,
                    nugetconfig: configFile,
                    targetframework: framework);
            }
            catch (PackageObtainException ex)
            {
                throw new GracefulException(
                    message:
                    $"Install failed. Failed to download package:{Environment.NewLine}" +
                    $"NuGet returned:{Environment.NewLine}" +
                    $"{Environment.NewLine}" +
                    $"{ex.Message}",
                    innerException: ex);
            }
            catch (ToolConfigurationException ex)
            {
                throw new GracefulException(
                    message:
                    $"Install failed. The settings file in the tool's NuGet package is not valid. Please contact the owner of the NuGet package.{Environment.NewLine}" +
                    $"The error was:{Environment.NewLine}" +
                    $"{Environment.NewLine}" +
                    $"{ex.Message}",
                    innerException: ex);
            }
        }
    }
}
