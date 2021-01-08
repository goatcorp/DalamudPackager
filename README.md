# DalamudPackager

This is an MSBuild task that is designed to simplify creating plugins for [Dalamud][dalamud].

Install the NuGet package `DalamudPackager`. When you build in release mode, a folder will be placed in your output directory
containing your plugin manifest and `latest.zip`.

## Table of contents

1. [DalamudPackager][toc-1]
2. [Table of contents][toc-2]
3. [Configuration][toc-3]
4. [Manifest generation][toc-4]
5. [YAML manifests][toc-5]
6. [`.zip` file generation][toc-6]
7. [Task attributes][toc-7]

[toc-1]: #dalamudpackager
[toc-2]: #table-of-contents
[toc-3]: #configuration
[toc-4]: #manifest-generation
[toc-5]: #yaml-manifests
[toc-6]: #zip-file-generation
[toc-7]: #task-attributes

## Configuration

If you need to additionally configure the task, create a file called `DalamudPackager.targets` in your project and use the
template below.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
    <Target Name="PackagePlugin" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
        <DalamudPackager
                ProjectDir="$(ProjectDir)"
                OutputPath="$(OutputPath)"
                AssemblyName="$(AssemblyName)"
                MakeZip="true"/>
    </Target>
</Project>
```

## Manifest generation

DalamudPackager reduces the amount of keys you need to include in your manifest, filling in the rest from sane defaults
or your assembly. You can, of course, specify everything manually.

```json5
{
    "Name": "Test Plugin",
    "Author": "You",
    "Description": "This is a test plugin",
    "RepoUrl": "https://example.com/"
}
```

Notice how keys like `AssemblyVersion` and `DalamudApiLevel` are missing. You can include these if you'd like, but
DalamudPackager will automatically do it for you when you build your project. If you build a project with this manifest
in Release mode, you will find the following JSON file in your output directory.

```json5
{
  "Author": "You",
  "Name": "Test Plugin",
  // this will be set to your AssemblyName automatically
  "InternalName": "TestPlugin",
  // this will be set to your assembly's version automatically
  "AssemblyVersion": "1.0.0.0",
  "Description": "This is a test plugin - this first line is a summary.\n\nDown here is a more detailed explanation of what the plugin\ndoes, manually wrapped to make sure it stays visible in the\ninstaller.",
  // this will be set to "any" automatically
  "ApplicableVersion": "any",
  "RepoUrl": "https://example.com/",
  // this will be set to 2 automatically
  "DalamudApiLevel": 2,
  // this will be set to 0 automatically
  "LoadPriority": 0
}
```

## YAML manifests

In addition, DalamudPackager allows you to use YAML, a more human-friendly format, for your manifest instead of JSON.
YAML uses **snake_case** for keys instead of PascalCase, like JSON. All the features of DalamudPackager work with YAML
manifests. Just end your manifest's file name with `.yaml` instead of `.json` to make use of it.

```yaml
name: Test Plugin
author: You
description: |-
  This is a test plugin - this first line is a summary.

  Down here is a more detailed explanation of what the plugin
  does, manually wrapped to make sure it stays visible in the
  installer.
repo_url: https://example.com/
```

## `.zip` file generation

DalamudPackager will also create a folder with `latest.zip` for you if `MakeZip` is `true`. Check your output directory
for a folder with the name of your assembly. Inside will be your manifest and `latest.zip`, ready for distribution or
PRs.

Note that the entire contents of your output directory will be zipped by default. Either turn off copy local for Dalamud
references, set up a task to clean your output directory, or use `Exclude` or `Include` if you want to change this.

## Task attributes

| Attribute | Description | Required | Default |
| --------- | ----------- | -------- | ------- |
| `ProjectDir` | This is the path where your `csproj` is located. You must set this to `$(ProjectDir)`. | **Yes** | *None* - set to `$(ProjectDir)` |
| `OutputPath` | This is the path that your assemblies are output to after build. You must set this to `$(OutputPath)`. | **Yes** | *None* - set to `$(OutputPath)` |
| `AssemblyName` | This is the name of the assembly that Dalamud will be loading. You used to need to specify this in your manifest as `InternalName`. | **Yes** | *None* - set to `$(AssemblyName)` |
| `ManifestType` | You can choose between `auto`, `json`, and `yaml` for your manifest file. `auto` will use `json` first, then fall back on `yaml`. | No | `auto` |
| `VersionComponents` | How many components of the assembly's version to include in the generated manifest. If you use semantic versioning, set this to `3`. | No | `4` |
| `MakeZip` | If this is `true`, a folder will be created in your `OutputPath` that contains your generated manifest and `latest.zip`, ready for PRing. | No | `false` |
| `Exclude` | Files to exclude from the zip if `MakeZip` is `true`. Mutually exclusive with `Include`. Files should be separated by a semicolon (`;`) and be relative to `OutputPath`. Files do not need to exist. | No | *None* |
| `Include` | Files to include in the zip if `MakeZip` is `true`. Mutually exclusive with `Exclude`. Files should be separated by a semicolon (`;`) and be relative to `OutputPath`. Files must exist. | No | *None* |

[dalamud]: https://github.com/goatcorp/Dalamud
