namespace Avalonia.Telemetry;

// The CI system a build ran under; see ContinuousIntegrationHelper. The order
// is fixed: version 1 and 2 binary records store the provider as its byte
// value, so a new provider goes at the end, after Generic.
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

    // Added for version 2 of the telemetry.
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

    // A CI system, but not one on this list.
    Generic,

    // Added for version 3 of the telemetry.
    Forgejo,
    GoCd,

    // nektos/act running a GitHub Actions workflow locally, not a CI service.
    Act,
}
