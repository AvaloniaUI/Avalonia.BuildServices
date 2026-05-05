namespace Avalonia.Telemetry;

public enum CiProvider
{
    None,
    Bamboo,
    Bitrise,
    SpaceAutomation,
    Jenkins,
    AppVeyor,
    GitLab,
    BitBucket,
    Travis,
    TeamCity,
    GitHubActions,
    AzurePipelines,
    
    //New providers for v2 of the telemetry 
    AwsCodeBuild,
    Buddy,
    Buildkite,
    CircleCi,
    Codefresh,
    CodeShip,
    Drone,
    Gitea,
    GoogleCloudBuild,
    Harness,
    OracleCloudBuild,
    Semaphore,
    WerckerOracle,
    Woodpecker,
    
    Generic, // We know we're running on a CI system but don't know which one 
}