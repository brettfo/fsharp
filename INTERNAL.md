# Links for internal team members to find build definitions, etc.

Note that usually only the most recent link in each section is interesting.  Older links are included for reference only.

## PR Build Definition

The PR build definition can be found [here](https://dev.azure.com/dnceng/public/_build?definitionId=496) or by
navigating through an existing PR.

## Signed Build Definitions

[VS 16.4 to current](https://dev.azure.com/dnceng/internal/_build?definitionId=499&_a=summary)

[VS 15.7 to 16.3](https://dev.azure.com/devdiv/DevDiv/_build/index?definitionId=8978)

[VS 15.6](https://dev.azure.com/devdiv/DevDiv/_build?definitionId=7239)

[VS 15.0 to 15.5](https://dev.azure.com/devdiv/DevDiv/_build?definitionId=5037)

## VS Insertion Generators

VS 16.4 to current - part of the build definition.  [See below](#vs-insertions-as-part-of-the-build-definition).

The following insertion generators are automatically invoked upon successful completion of a signed build in each of
their respective branches.

[VS 16.3](https://dev.azure.com/devdiv/DevDiv/_release?definitionId=1839&_a=releases)

[VS 16.2](https://dev.azure.com/devdiv/DevDiv/_release?definitionId=1699&_a=releases)

[VS 16.1](https://dev.azure.com/devdiv/DevDiv/_release?definitionId=1669&_a=releases)

VS 16.0 and prior were done manually

## VS Insertions as part of the build definition

Starting with the 16.4 release and moving forwards, the VS insertion is generated as part of the build.  The relevant
bits can be found near the bottom of [`azure-pipelines.yml`](azure-pipelines.yml) under the `VS Insertion` header.  The
interesting parameters are `componentBranchName` and `insertTargetBranch`.  In short, when an internal signed build
completes and the name of the branch built exactly equals the value in the `componentBranchName` parameter, a component
insertion into VS will be created into the `insertTargetBranch` branch.  The link to the insertion PR will be found
near the bottom of the build under the title 'Insert into VS'.  Examine the log for 'Insert VS Payload' and near the
bottom you'll see a line that looks like `Created request #xxxxxx at https://...`.

To see all insertions created this way (possibly including for other internal teams), check
[here](https://dev.azure.com/devdiv/DevDiv/_git/VS/pullrequests?createdBy=122d5278-3e55-4868-9d40-1e28c2515fc4&_a=active).

## Per-build package uploads

Per-build package uploads are managed by the internal `darc` tool that comes with the Arcade SDK.  Updating the package
subscriptions requires:

1. Installing the latest `darc` tool: `.\eng\common\darc-init.ps1`

2. **(Only required once)** Authenticate: `darc authenticate`

3. Review package channel subscriptions: `darc get-default-channels --source-repo dotnet/fsharp`

   E.g., `... release/dev16.5 -> .NET Core SDK 3.1.2xx` indicates that the packages produced from a build of the
   `release/dev16.5` branch will be published to the `.NET Core SDK 3.1.2xx` package feed.

Per-build packages should come from `master` and be published to the `TODO: fixme` feed.

## Less interesting links

[Nightly VSIX (master) uploader](https://dev.azure.com/dnceng/internal/_release?_a=releases&definitionId=70).  Uploads
a package from every build of `master` to the [Nightly VSIX feed](README.md#using-nightly-releases-in-visual-studio).

[Nightly VSIX (preview) uploader](https://dev.azure.com/dnceng/internal/_release?_a=releases&definitionId=71).  Uploads
a package from every build of the branch that corresponds to the current Visual Studio preview to the
[Preview VSIX feed](README.md#using-nightly-releases-in-visual-studio).

[MyGet package uploader](https://dev.azure.com/dnceng/internal/_release?_a=releases&definitionId=69).  Uploads various
packages for internal consumption.  Feed URL is `https://dotnet.myget.org/F/fsharp/api/v3/index.json`.

[Internal source mirror](https://dev.azure.com/dnceng/internal/_git/dotnet-fsharp).
