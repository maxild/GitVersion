using GitVersion.Configuration;
using GitVersion.OutputVariables;
using GitVersion.VersionCalculation;

namespace GitVersion.Tests.IntegrationTests;

public class MyScenarios
{
    [Test]
    public void CompareTheModesUsedInGitFlowOnALinearMainline()
    {
        var manualDeployment = GitFlowConfigurationBuilder.New
            .WithBranch(ConfigurationConstants.MainBranchKey, b => b
                .WithLabel("dev").WithDeploymentMode(DeploymentMode.ManualDeployment))
            .Build();
        var continuousDelivery = GitFlowConfigurationBuilder.New
            .WithBranch(ConfigurationConstants.MainBranchKey, b => b
                .WithLabel("dev").WithDeploymentMode(DeploymentMode.ContinuousDelivery))
            .Build();

        using EmptyRepositoryFixture fixture = new(); // main

        fixture.MakeATaggedCommit("v1.0.0");

        fixture.AssertFullSemver("1.0.0", manualDeployment);
        fixture.AssertFullSemver("1.0.0", continuousDelivery);

        fixture.MakeACommit();

        fixture.AssertFullSemver("1.0.1-dev.1+1", manualDeployment);
        fixture.AssertFullSemver("1.0.1-dev.1", continuousDelivery);

        fixture.MakeACommit();

        fixture.AssertFullSemver("1.0.1-dev.1+2", manualDeployment);
        fixture.AssertFullSemver("1.0.1-dev.2", continuousDelivery);

        fixture.ApplyTag("1.0.1-dev.2");

        fixture.AssertFullSemver("1.0.1-dev.2", manualDeployment);
        fixture.AssertFullSemver("1.0.1-dev.2", continuousDelivery);

        fixture.MakeACommit();

        fixture.AssertFullSemver("1.0.1-dev.3+1", manualDeployment);
        fixture.AssertFullSemver("1.0.1-dev.3", continuousDelivery);

        fixture.MakeACommit();

        fixture.AssertFullSemver("1.0.1-dev.3+2", manualDeployment);
        fixture.AssertFullSemver("1.0.1-dev.4", continuousDelivery);

        fixture.ApplyTag("1.0.1");

        fixture.AssertFullSemver("1.0.1", manualDeployment);
        fixture.AssertFullSemver("1.0.1", continuousDelivery);

        fixture.MakeACommit();

        fixture.AssertFullSemver("1.0.2-dev.1+1", manualDeployment);
        fixture.AssertFullSemver("1.0.2-dev.1", continuousDelivery);
    }

    [Test]
    public void PlayingWithManualDeploymentVersioningMode()
    {
        var configuration = GitHubFlowConfigurationBuilder.New
            .WithLabel(null)
            .WithBranch("main", branchBuilder => branchBuilder
                .WithDeploymentMode(DeploymentMode.ManualDeployment)
                .WithLabel("beta")
                .WithIncrement(IncrementStrategy.Patch)
            ).Build();

        using var fixture = new EmptyRepositoryFixture();

        fixture.MakeATaggedCommit("1.0.0");
        fixture.AssertFullSemver("1.0.0", configuration);

        fixture.MakeACommit();

        // fixture.AssertFullSemver("1.0.1-1+1", configuration);
        fixture.AssertFullSemver("1.0.1-beta.1+1", configuration);

        fixture.ApplyTag("1.0.1-alpha.1"); // This tag is ignored, because it is less than previous version

        // fixture.AssertFullSemver("1.0.1-alpha.1", configuration);
        fixture.AssertFullSemver("1.0.1-beta.1+1", configuration);
        //
        fixture.MakeACommit();
        //
        fixture.AssertFullSemver("1.0.1-beta.1+2", configuration);
    }

    [Test]
    public static void PrereleaseNumberIsNotMonotonicIncreasing() // https://github.com/GitTools/GitVersion/discussions/4664
    {
        var configuration = GitFlowConfigurationBuilder.New
            .WithMajorVersionBumpMessage("BREAKING CHANGE")
            .WithMinorVersionBumpMessage("^(feat)")
            .WithPatchVersionBumpMessage("^(build|chore|ci|docs|fix|perf|refactor|revert|style|test)")
            .WithCommitMessageIncrementing(CommitMessageIncrementMode.Enabled)
            .WithBranch("develop", b => b
                .WithLabel("develop")
                .WithRegularExpression("develop") //
                .WithDeploymentMode(DeploymentMode.ContinuousDelivery) // indsat af mig
            )
            .WithBranch("main", b => b
                .WithLabel("")
                .WithRegularExpression("main")
                .WithDeploymentMode(DeploymentMode.ContinuousDelivery) // indsat af mig
            )
            .WithBranch("hotfix", b => b
                .WithLabel("hotfix")
                .WithRegularExpression("^hotfix?[/-]")
                .WithDeploymentMode(DeploymentMode.ContinuousDelivery)
            )
            .Build();

        configuration.CommitMessageIncrementing.ShouldBe(CommitMessageIncrementMode.Enabled);
        // configuration.Branches["main"].DeploymentMode.ShouldBeNull(); // FIXME: Hvad betyder dette?
        configuration.Branches["main"].DeploymentMode.ShouldBe(DeploymentMode.ContinuousDelivery);
        configuration.Branches["develop"].DeploymentMode.ShouldBe(DeploymentMode.ContinuousDelivery);
        configuration.Branches["hotfix"].DeploymentMode.ShouldBe(DeploymentMode.ContinuousDelivery);

        // Create a git repo with a develop branch off main which is a single commit ahead of main branch
        // main will be git tagged with 1.0.0 before branching develop
        using var fixture = new BaseGitFlowRepositoryFixture("1.0.0");

        // Implicit commit on develop branch here

        fixture.AssertFullSemver("1.1.0-develop.1", configuration);

        // Feature
        fixture.MakeACommit("feat: f1"); // bump minor
        {
            var variables = MyAssertFullSemver(fixture, "1.1.0-develop.2", configuration);
            // Gets the commit used as the counting anchor, or null when counting all reachable history
            variables.VersionSourceSemVer.ShouldBe("1.0.0");
            // Gets the count of non-ignored commits beyond the counting anchor.
            variables.VersionSourceDistance.ShouldBe("2");
        }

        // Dette sker i main branch
        // Release 1.1.0 by merging develop into develop and git tag with 1.1.0
        fixture.Checkout("main");
        fixture.MergeNoFF("develop");
        fixture.AssertFullSemver("1.1.0-3", configuration); // empty label (increment = minor paa main)
        fixture.ApplyTag("1.1.0"); // Dette aendrer version source, det dette tag er reachable fra develop sporet
        fixture.AssertFullSemver("1.1.0", configuration);

        // Lav mere arbejde i develop
        fixture.Checkout("develop");
        fixture.MakeACommit("feat: f2");
        {
            var variables = MyAssertFullSemver(fixture, "1.2.0-develop.1", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.0"); // counting anchor is CHANGED!!!!
            variables.VersionSourceDistance.ShouldBe("1"); // distance reset to "1" because version base is higher than version source
        }

        // Hotfix baseret paa main branch
        fixture.Checkout("main");
        fixture.BranchTo("hotfix/hf");
        fixture.MakeACommit("fix: applied hotfix");
        {
            var variables = MyAssertFullSemver(fixture, "1.1.1-hotfix.1", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.0");
            variables.VersionSourceDistance.ShouldBe("1");
        }

        // Merge hotfix->develop
        fixture.Checkout("develop");
        fixture.MergeNoFF("hotfix/hf");
        {
            // Q: Hvordan bliver distance = 3 beregnet?
            var variables = MyAssertFullSemver(fixture, "1.2.0-develop.3", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.0");
            variables.VersionSourceDistance.ShouldBe("3"); // merge hotfix -> develop changes prerelease number (# reachable commits)
        }

        // Lav mere arbejde i develop
        fixture.MakeACommit("chore: foobar");
        {
            var variables = MyAssertFullSemver(fixture, "1.2.0-develop.4", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.0");
            variables.VersionSourceDistance.ShouldBe("4");
        }

        // Merge hotfix->main
        fixture.Checkout("main");
        fixture.MergeNoFF("hotfix/hf");
        fixture.Remove("hotfix/hf");
        {
            // paa main uden git tag
            var variables = MyAssertFullSemver(fixture, "1.1.1-2", configuration); // no git tag, distance = 2
            variables.VersionSourceSemVer.ShouldBe("1.1.0");
            variables.VersionSourceDistance.ShouldBe("2");
        }

        // tag release on main (this shifts version source to tagged commit!!!!)
        fixture.ApplyTag("1.1.1"); // If the hotfix release is not tagged, develop is not affected.
        fixture.AssertFullSemver("1.1.1", configuration);
        {
            // paa main med git tag paa HEAD
            var variables = MyAssertFullSemver(fixture, "1.1.1", configuration);
            variables.VersionSourceSha.ShouldBe(variables.Sha); // VIGTIGT
            variables.VersionSourceSemVer.ShouldBe("1.1.1");
            variables.VersionSourceDistance.ShouldBe("0");
        }

        // Feature 5
        fixture.Checkout("develop");
        {
            // BUG: Burde vaere develop.4 - Hotfix merged to main and tagged has side-effect
            var variables = MyAssertFullSemver(fixture, "1.2.0-develop.3", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.1");
            variables.VersionSourceDistance.ShouldBe("3");
        }

        fixture.MakeACommit("feat: f3");
        {
            // BUG: Burde vaere develop.5
            var variables = MyAssertFullSemver(fixture, "1.2.0-develop.4", configuration);
            variables.VersionSourceSemVer.ShouldBe("1.1.1");
            variables.VersionSourceDistance.ShouldBe("4");
        }

        // Med denne muligt at se hvilken version source (anchor) gitversion beregner
        static GitVersionVariables MyAssertFullSemver(RepositoryFixtureBase fixture, string expectedFullSemver,
                                                      IGitVersionConfiguration configuration)
        {
            var variables = fixture.GetVersion(configuration);
            if (!string.IsNullOrEmpty(expectedFullSemver))
            {
                variables.FullSemVer.ShouldBe(expectedFullSemver);
            }
            // Diagrammet viser hvad GitVersion beslutter SemVer til (uanset expectedFullSemver)
            fixture.SequenceDiagram.NoteOver(variables.FullSemVer, fixture.Repository.Head.FriendlyName, color: "#D3D3D3");
            return variables;
        }
    }

    [Test]
    public void PrereleaseNumberIsNotMonotonicIncreasing_Simplified()
    {
        var configuration = GitFlowConfigurationBuilder.New.Build();

        using var fixture = new EmptyRepositoryFixture(); // main
        fixture.MakeATaggedCommit("1.0.0");
        fixture.AssertFullSemver("1.0.0", configuration);

        fixture.BranchTo("develop");
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);

        fixture.BranchTo("hotfix/next");
        fixture.MakeACommit();
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.0.1-beta.1+3", configuration); // BUG: hotfix indeholder 1 commit fra develop

        // Develop branch before hotfix is merged to main
        fixture.Checkout("develop");
        fixture.MakeACommit();
        fixture.AssertFullSemver("1.1.0-alpha.2", configuration);

        // merge hotfix into main
        fixture.Checkout("hotfix/next");
        fixture.MergeTo("main", removeBranchAfterMerging: true);

        fixture.AssertFullSemver("1.0.1-4", configuration); // main uden tag, og uden label, distance = 4
        fixture.ApplyTag("1.0.1");
        fixture.AssertFullSemver("1.0.1", configuration);

        // PROBLEM: PrereleaseNumber 2 -> 1 (even though develop branch unchanged)
        fixture.Checkout("develop");
        fixture.AssertFullSemver("1.1.0-alpha.1", configuration);
    }

    // See https://github.com/GitTools/GitVersion/discussions/4664#discussioncomment-17704570
    //
    // git init -b main && git commit --allow-empty -m init && git tag 1.0.0
    // git checkout -b develop
    // for i in 1 2 3 4 5; do git commit --allow-empty -m "d$i"; done   # develop -> 1.1.0-beta.5
    //
    // git checkout -b hotfix/1.0.1 main
    // git commit --allow-empty -m fix
    //
    // # merge the hotfix into develop BEFORE it is tagged on main
    // git checkout develop && git merge --no-ff hotfix/1.0.1           # develop -> 1.1.0-beta.7
    //
    // # finish the hotfix on main and tag it
    // git checkout main && git merge --no-ff hotfix/1.0.1 && git tag 1.0.1
    // git checkout develop                                             # develop -> 1.1.0-beta.6  (was beta.7 -> REGRESSION)
}
