using System;

namespace Avalonia.Telemetry;

internal static class ContinuousIntegrationHelper
{
    // The first rule that matches wins, so the order below matters.
    public static CiProvider DetectCiProvider(BuildEnvironment environment)
    {
        // Runners built on act, which all set GITHUB_ACTIONS too, so they go first

        // Gitea Actions
        if (environment.Contains("GITEA_ACTIONS"))
            return CiProvider.Gitea;

        // Forgejo Actions - Gitea fork, runs Codeberg's CI
        if (environment.Contains("FORGEJO_ACTIONS"))
            return CiProvider.Forgejo;

        // nektos/act - a workflow run locally in Docker, usually on a developer's machine
        if (environment.HasValue("ACT", "true") && environment.Contains("GITHUB_ACTIONS"))
            return CiProvider.Act;

        // Most common for .NET projects

        // GitHub Actions - Very popular for open source and modern .NET
        if (environment.Contains("GITHUB_ACTIONS"))
            return CiProvider.GitHubActions;

        // Azure DevOps/Pipelines - Microsoft's own, huge in enterprise .NET
        if (environment.Contains("TF_BUILD"))
            return CiProvider.AzurePipelines;

        // Jenkins - Still massive in enterprise. Builds always get a build
        // number; without one, JENKINS_URL is likely just the Jenkins CLI's
        // server address and JENKINS_HOME a machine with Jenkins installed
        if ((environment.Contains("JENKINS_HOME") ||
             environment.Contains("JENKINS_URL") ||
             environment.Contains("HUDSON_URL")) &&
            (environment.Contains("BUILD_ID") || environment.Contains("BUILD_NUMBER")))
            return CiProvider.Jenkins;
        
        // TeamCity - JetBrains, very popular in .NET shops
        if (environment.Contains("TEAMCITY_VERSION"))
            return CiProvider.TeamCity;
        
        // GitLab - Common in enterprises that self-host
        if (environment.Contains("GITLAB_CI"))
            return CiProvider.GitLab;
        
        // AppVeyor - Was THE .NET CI for a long time
        if (environment.Contains("APPVEYOR"))
            return CiProvider.AppVeyor;
        
        // Growing in .NET ecosystem
        
        // Bitbucket Pipelines - Atlassian stack enterprises
        if (environment.Contains("BITBUCKET_PIPELINE_UUID"))
            return CiProvider.BitBucket;
        
        // CircleCI - Growing .NET support
        if (environment.Contains("CIRCLECI"))
            return CiProvider.CircleCi;
        
        // AWS CodeBuild - Enterprises on AWS
        if (environment.Contains("CODEBUILD_BUILD_ID") || environment.Contains("CODEBUILD_BUILD_ARN"))
            return CiProvider.AwsCodeBuild;
        
        // Google Cloud Build - Enterprises on GCP
        if (environment.Contains("BUILDER_OUTPUT") || 
            (environment.Contains("BUILD_ID") && environment.Contains("PROJECT_ID") && environment.Contains("REPO_NAME")))
            return CiProvider.GoogleCloudBuild;
        
        // Enterprise/Specialised
        
        // JetBrains Space - Growing with Rider users
        if (environment.Contains("JB_SPACE_PROJECT_KEY"))
            return CiProvider.SpaceAutomation;
        
        // Harness - Enterprise CD
        if (environment.Contains("HARNESS_BUILD_ID") || environment.Contains("HARNESS_ACCOUNT_ID"))
            return CiProvider.Harness;
        
        // Bamboo - Atlassian enterprises
        if (environment.Contains("bamboo_planKey"))
            return CiProvider.Bamboo;
        
        // Oracle Cloud - Some enterprises
        if (environment.Contains("OCI_BUILD_RUN_ID") || environment.Contains("OCI_BUILD_ID") || environment.Contains("OCI_PRIMARY_SOURCE_DIR"))
            return CiProvider.OracleCloudBuild;

        // GoCD - Thoughtworks, self-hosted; sets no CI variable, so the fallback misses it
        if (environment.Contains("GO_PIPELINE_LABEL"))
            return CiProvider.GoCd;

        // Less common in .NET
        
        // Travis - More common for non-.NET but still used
        if (environment.Contains("TRAVIS"))
            return CiProvider.Travis;
        
        // Buildkite
        if (environment.Contains("BUILDKITE"))
            return CiProvider.Buildkite;
        
        // Drone
        if (environment.Contains("DRONE"))
            return CiProvider.Drone;
        
        // Semaphore
        if (environment.Contains("SEMAPHORE") || environment.Contains("SEMAPHORE_BUILD_NUMBER"))
            return CiProvider.Semaphore;
        
        // CodeShip
        if (environment.HasValue("CI_NAME", "codeship", StringComparison.Ordinal))
            return CiProvider.CodeShip;
        
        // Codefresh - Kubernetes focused
        if (environment.Contains("CF_BUILD_ID") || environment.Contains("CF_BUILD_URL"))
            return CiProvider.Codefresh;
        
        // Buddy
        if (environment.Contains("BUDDY_WORKSPACE_ID") || environment.Contains("BUDDY_EXECUTION_ID"))
            return CiProvider.Buddy;
        
        // Bitrise - Mobile focused
        if (environment.Contains("BITRISE_BUILD_URL"))
            return CiProvider.Bitrise;
        
        // Wercker (Oracle)
        if (environment.Contains("WERCKER") || environment.Contains("WERCKER_RUN_ID"))
            return CiProvider.WerckerOracle;

        // Woodpecker - Drone fork
        if (environment.Contains("WOODPECKER") || 
            (environment.Contains("CI_PIPELINE_NUMBER") && environment.Contains("CI_WORKSPACE")))
            return CiProvider.Woodpecker;
        
        // Fallbacks
        
        // Defensive Azure fallback if env scrubbing clipped TF_BUILD but left System.*
        if (environment.Contains("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI") || environment.Contains("SYSTEM_COLLECTIONURI"))
            return CiProvider.AzurePipelines;
        
        // Last-resort portable CI flag used by many platforms. Some set their
        // own name rather than true (CI=woodpecker); false and 0 mean not CI
        var ci = environment.Get("CI");
        if (!string.IsNullOrEmpty(ci) && ci != "0" && !string.Equals(ci, "false", StringComparison.OrdinalIgnoreCase))
            return CiProvider.Generic;

        // CI systems we have no rule for that name themselves (CI_NAME=sourcehut)
        if (environment.IsSet("CI_NAME"))
            return CiProvider.Generic;

        return CiProvider.None;
    }
}

