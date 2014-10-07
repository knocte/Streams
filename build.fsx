// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#I "packages/FAKE/tools"
#r "packages/FAKE/tools/FakeLib.dll"
//#load "packages/SourceLink.Fake/tools/SourceLink.fsx"
open System
open Fake 
open Fake.Git
open Fake.ReleaseNotesHelper
open Fake.AssemblyInfoFile
//open SourceLink

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "Streams"
let authors = ["Nessos Information Technologies, Nick Palladinos"]
let summary = "A lightweight F#/C# library for efficient functional-style pipelines on streams of data."

let description = """
    A lightweight F#/C# library for efficient functional-style pipelines on streams of data.
"""

let tags = "F#/C# Streams"

let gitHome = "https://github.com/nessos"
let gitName = "Streams"
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/nessos"


let testAssemblies = 
    [
        "bin/Streams.Tests.exe"
        "bin/Streams.Tests.CSharp.exe"
        "bin/Streams.Cloud.Tests.exe"
    ]

//
//// --------------------------------------------------------------------------------------
//// The rest of the code is standard F# build script 
//// --------------------------------------------------------------------------------------

//// Read release notes & version info from RELEASE_NOTES.md
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

module Streams =
    let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")
    let nugetVersion = release.NugetVersion

module CloudStreams =
    let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES_CLOUDSTREAM.md")
    let nugetVersion = release.NugetVersion

Target "BuildVersion" (fun _ ->
    Shell.Exec("appveyor", sprintf "UpdateBuild -Version \"%s\"" Streams.nugetVersion) |> ignore
)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let attributes version =
        [ 
            Attribute.Title project
            Attribute.Product project
            Attribute.Company "Nessos Information Technologies"
            Attribute.Version version
            Attribute.FileVersion version
        ]

    CreateFSharpAssemblyInfo "src/Streams.Core/AssemblyInfo.fs" <| attributes Streams.release.AssemblyVersion
    CreateCSharpAssemblyInfo "src/Streams.Core.CSharp/Properties/AssemblyInfo.cs" <| attributes Streams.release.AssemblyVersion
    CreateFSharpAssemblyInfo "src/Streams.Core/AssemblyInfo.fs" <| attributes CloudStreams.release.AssemblyVersion
)


// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "RestorePackages" (fun _ ->
    !! "./**/packages.config"
    |> Seq.iter (RestorePackage (fun p -> { p with ToolPath = "./.nuget/NuGet.exe" }))
)

Target "Clean" (fun _ ->
    CleanDirs (!! "**/bin/Release/")
    CleanDirs (!! "**/bin/Debug/")
    CleanDir "bin/"
)

//
//// --------------------------------------------------------------------------------------
//// Build library & test project

let configuration = environVarOrDefault "Configuration" "Release"

Target "Build" (fun _ ->
    // Build the rest of the project
    { BaseDirectory = __SOURCE_DIRECTORY__
      Includes = [ project + ".sln" ]
      Excludes = [] } 
    |> MSBuild "" "Build" ["Configuration", configuration]
    |> Log "AppBuild-Output: "
)


// --------------------------------------------------------------------------------------
// Run the unit tests using test runner & kill test runner when complete

Target "RunTests" (fun _ ->
    let nunitVersion = GetPackageVersion "packages" "NUnit.Runners"
    let nunitPath = sprintf "packages/NUnit.Runners.%s/tools" nunitVersion
    ActivateFinalTarget "CloseTestRunner"

    testAssemblies
    |> NUnit (fun p ->
        { p with
            Framework = "v4.0.30319"
            ToolPath = nunitPath
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" })
)

FinalTarget "CloseTestRunner" (fun _ ->  
    ProcessHelper.killProcess "nunit-agent.exe"
)
//
//// --------------------------------------------------------------------------------------
//// Build a NuGet package

Target "NuGet" (fun _ ->
    let nugetPath = ".nuget/NuGet.exe"

    let mkNuGetPackage project =
        // Format the description to fit on a single line (remove \r\n and double-spaces)
        let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
        NuGet (fun p -> 
            { p with   
                Authors = authors
                Project = project
                Summary = summary
                Description = description
                Version = Streams.nugetVersion
                ReleaseNotes = String.concat " " Streams.release.Notes
                Tags = tags
                OutputPath = "nuget"
                ToolPath = nugetPath
                AccessKey = getBuildParamOrDefault "nugetkey" ""
                Publish = hasBuildParam "nugetkey" })
            ("nuget/" + project + ".nuspec")

    mkNuGetPackage "Streams"
    mkNuGetPackage "Streams.CSharp"

    NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = "Streams.Cloud"
            Summary = summary
            Description = description
            Version = CloudStreams.nugetVersion
            ReleaseNotes = String.concat " " CloudStreams.release.Notes
            Tags = tags
            OutputPath = "nuget"
            Dependencies = 
                [
                    "Streams",      RequireExactly "0.2.0"
                    "MBrace.Core",  RequireExactly "0.5.7-alpha"
                ]
            ToolPath = nugetPath
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        ("nuget/Streams.Cloud.nuspec")
)


Target "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "Prepare" DoNothing
Target "PrepareRelease" DoNothing
Target "All" DoNothing

"Clean"
  ==> "RestorePackages"
  ==> "AssemblyInfo"
  ==> "Prepare"
  ==> "Build"
  ==> "RunTests"
  ==> "All"

"All"
  ==> "PrepareRelease" 
  ==> "NuGet"
  ==> "Release"

RunTargetOrDefault "Release"
//RunTargetOrDefault "All"
