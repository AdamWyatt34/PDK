using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers;
using PDK.Providers.AzureDevOps;
using PDK.Providers.AzureDevOps.Templates;
using Xunit;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

/// <summary>
/// Template expansion of Azure pipelines: directives, scalar expressions, parameters, compile-time variables,
/// template files, extends and the errors they produce. Files are written to a temporary directory so that
/// template paths resolve like they do for a real pipeline file.
/// </summary>
public sealed class AzureTemplateProcessorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pdk-azure-templates-" + Guid.NewGuid().ToString("N"));
    private readonly AzureDevOpsParser _parser = new();

    public AzureTemplateProcessorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private IReadOnlyList<string> Warnings => ((IPipelineParserWarnings)_parser).Warnings;

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Pipeline ParseFile(string relativePath, params (string Name, string Value)[] parameters) =>
        _parser.ParseFile(Path.Combine(_root, relativePath), Options(parameters)).GetAwaiter().GetResult();

    private Pipeline Parse(string yaml, params (string Name, string Value)[] parameters) => _parser.Parse(yaml, Options(parameters));

    private static PipelineParseOptions Options(params (string Name, string Value)[] parameters) => new()
    {
        Parameters = parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase)
    };

    #region if / elseif / else

    private const string BranchingPipeline = @"
parameters:
  - name: env
    type: string
    default: dev
jobs:
  - job: Build
    ${{ if eq(parameters.env, 'prod') }}:
      timeoutInMinutes: 30
    ${{ elseif eq(parameters.env, 'staging') }}:
      timeoutInMinutes: 15
    ${{ else }}:
      timeoutInMinutes: 5
    steps:
      - script: echo build
      - ${{ if eq(parameters.env, 'prod') }}:
        - script: echo prod-only
      - ${{ elseif eq(parameters.env, 'staging') }}:
        - script: echo staging-only
      - ${{ else }}:
        - script: echo dev-only
";

    [Theory]
    [InlineData("dev", 5, "echo dev-only")]
    [InlineData("staging", 15, "echo staging-only")]
    [InlineData("prod", 30, "echo prod-only")]
    public void Branches_OnMappingsAndSequences_SelectTheMatchingBranch(string env, int timeout, string script)
    {
        var job = Parse(BranchingPipeline, ("env", env)).Jobs["Build"];

        job.Timeout.Should().Be(TimeSpan.FromMinutes(timeout));
        job.Steps.Select(s => s.Script).Should().Equal("echo build", script);
    }

    [Fact]
    public void If_WithMappingValueOnListItem_InsertsOneItem()
    {
        var yaml = @"
parameters:
  - name: extra
    type: boolean
    default: true
steps:
  - script: echo one
  - ${{ if parameters.extra }}:
      script: echo two
";

        Parse(yaml).Jobs["default"].Steps.Select(s => s.Script).Should().Equal("echo one", "echo two");
    }

    [Fact]
    public void Else_WithoutIf_Throws()
    {
        var yaml = @"
steps:
  - script: echo one
  - ${{ else }}:
    - script: echo two
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("*'${{ else }}' must directly follow a '${{ if }}'*line 4*");
    }

    [Fact]
    public void If_WithoutCondition_Throws()
    {
        var yaml = @"
steps:
  - ${{ if }}:
    - script: echo two
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("*Invalid directive '${{ if }}'*needs a condition*");
    }

    [Fact]
    public void DirectiveInsertingAnExistingKey_ThrowsDuplicateKey()
    {
        var yaml = @"
jobs:
  - job: Build
    displayName: Explicit
    ${{ if true }}:
      displayName: Inserted
    steps:
      - script: echo hi
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Duplicate key 'displayName'*line 6*");
    }

    #endregion

    #region each

    [Fact]
    public void Each_OverList_InsertsOneJobPerItem()
    {
        var yaml = @"
parameters:
  - name: configurations
    type: object
    default: [Debug, Release]
jobs:
  - ${{ each config in parameters.configurations }}:
    - job: Build_${{ config }}
      displayName: Build ${{ config }}
      steps:
        - script: dotnet build -c ${{ config }}
";

        var pipeline = Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Build_Debug", "Build_Release");
        pipeline.Jobs["Build_Release"].Name.Should().Be("Build Release");
        pipeline.Jobs["Build_Release"].Steps[0].Script.Should().Be("dotnet build -c Release");
    }

    [Fact]
    public void Each_OverMapping_ExposesKeyAndValue()
    {
        var yaml = @"
parameters:
  - name: tags
    type: object
    default:
      owner: platform
      tier: gold
variables:
  - ${{ each pair in parameters.tags }}:
    - name: tag.${{ pair.key }}
      value: ${{ pair.value }}
steps:
  - script: echo $(tag.owner)
";

        var pipeline = Parse(yaml);

        pipeline.Variables.Should().Equal(new Dictionary<string, string> { ["tag.owner"] = "platform", ["tag.tier"] = "gold" });
    }

    [Fact]
    public void Each_OverListOfObjects_AllowsPropertyAccessAndStructuralInsertion()
    {
        var yaml = @"
parameters:
  - name: extraSteps
    type: stepList
    default:
      - script: echo first
        displayName: First
      - script: echo second
        displayName: Second
steps:
  - ${{ each step in parameters.extraSteps }}:
    - ${{ step }}
  - ${{ each step in parameters.extraSteps }}:
    - script: echo again ${{ step.displayName }}
";

        var steps = Parse(yaml).Jobs["default"].Steps;

        steps.Select(s => s.Script).Should().Equal("echo first", "echo second", "echo again First", "echo again Second");
        steps[0].Name.Should().Be("First");
    }

    [Fact]
    public void Each_OverNonList_ThrowsNamingTheDirective()
    {
        var yaml = @"
parameters:
  - name: name
    type: string
    default: hello
steps:
  - ${{ each item in parameters.name }}:
    - script: echo ${{ item }}
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("'${{ each item in parameters.name }}' cannot iterate over a string*");
    }

    [Fact]
    public void Each_WithMalformedHeader_Throws()
    {
        var yaml = @"
steps:
  - ${{ each parameters.items }}:
    - script: echo hi
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("*must be written as ${{ each item in parameters.items }}*");
    }

    [Fact]
    public void NestedDirectives_WrapJobsWithExtraSteps()
    {
        // The pattern from the Azure docs: iterate jobs, copy every property except steps, wrap the steps
        var yaml = @"
parameters:
  - name: jobs
    type: jobList
    default:
      - job: A
        displayName: Job A
        steps:
          - script: echo a
      - job: B
        steps:
          - script: echo b
jobs:
  - ${{ each job in parameters.jobs }}:
    - ${{ each pair in job }}:
        ${{ if ne(pair.key, 'steps') }}:
          ${{ pair.key }}: ${{ pair.value }}
      steps:
        - script: echo pre
        - ${{ job.steps }}
        - script: echo post
";

        var pipeline = Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("A", "B");
        pipeline.Jobs["A"].Name.Should().Be("Job A");
        pipeline.Jobs["A"].Steps.Select(s => s.Script).Should().Equal("echo pre", "echo a", "echo post");
        pipeline.Jobs["B"].Steps.Select(s => s.Script).Should().Equal("echo pre", "echo b", "echo post");
    }

    #endregion

    #region insert and scalar expressions

    [Fact]
    public void Insert_MergesAnObjectParameterIntoTheMapping()
    {
        var yaml = @"
parameters:
  - name: jobSettings
    type: object
    default:
      displayName: Inserted name
      timeoutInMinutes: 7
jobs:
  - job: Build
    ${{ insert }}: ${{ parameters.jobSettings }}
    steps:
      - script: echo hi
";

        var job = Parse(yaml).Jobs["Build"];

        job.Name.Should().Be("Inserted name");
        job.Timeout.Should().Be(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void Insert_OnListItem_Throws()
    {
        var yaml = @"
steps:
  - ${{ insert }}:
      script: echo hi
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("'${{ insert }}' can only be used inside a mapping*");
    }

    [Fact]
    public void ScalarExpressions_AreSubstitutedAsText_LeavingMacrosAndRuntimeExpressionsAlone()
    {
        var yaml = @"
parameters:
  - name: flag
    type: boolean
    default: true
  - name: count
    type: number
    default: 3
  - name: name
    type: string
    default: World
steps:
  - script: echo ${{ parameters.name }} ${{ parameters.flag }} ${{ parameters.count }} $(macro) $[ variables.runtime ]
    displayName: Hello ${{ parameters.name }}
    env:
      ${{ upper(parameters.name) }}_KEY: value-${{ parameters.count }}
";

        var step = Parse(yaml).Jobs["default"].Steps[0];

        step.Script.Should().Be("echo World True 3 $(macro) $[ variables.runtime ]");
        step.Name.Should().Be("Hello World");
        step.Environment.Should().Equal(new Dictionary<string, string> { ["WORLD_KEY"] = "value-3" });
    }

    [Fact]
    public void WholeValueExpressions_ReplaceTheNodeStructurally()
    {
        var yaml = @"
parameters:
  - name: buildSteps
    type: stepList
    default:
      - script: echo one
      - script: echo two
  - name: deps
    type: object
    default: [A, B]
jobs:
  - job: A
    steps:
      - script: echo a
  - job: B
    steps:
      - script: echo b
  - job: C
    dependsOn: ${{ parameters.deps }}
    steps: ${{ parameters.buildSteps }}
";

        var job = Parse(yaml).Jobs["C"];

        job.DependsOn.Should().Equal("A", "B");
        job.Steps.Select(s => s.Script).Should().Equal("echo one", "echo two");
    }

    [Fact]
    public void ListValuedExpressionAsListItem_IsFlattenedIntoTheList()
    {
        var yaml = @"
parameters:
  - name: middle
    type: stepList
    default:
      - script: echo two
      - script: echo three
steps:
  - script: echo one
  - ${{ parameters.middle }}
  - script: echo four
";

        Parse(yaml).Jobs["default"].Steps.Select(s => s.Script).Should().Equal("echo one", "echo two", "echo three", "echo four");
    }

    [Fact]
    public void ObjectInterpolatedIntoText_ThrowsWithSuggestion()
    {
        var yaml = @"
parameters:
  - name: obj
    type: object
    default: { a: 1 }
steps:
  - script: echo ${{ parameters.obj }}
";

        var act = () => Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("produced an object (mapping), which cannot be inserted into text");
        exception.Suggestions.Should().Contain(s => s.Contains("convertToJson"));
    }

    [Fact]
    public void EmptyStringResult_StaysAnEmptyString()
    {
        var yaml = @"
parameters:
  - name: condition
    type: string
    default: ''
steps:
  - script: echo hi
    condition: ${{ parameters.condition }}
    displayName: ${{ parameters.condition }}
";

        var step = Parse(yaml).Jobs["default"].Steps[0];

        step.Condition.Should().BeNull();
        step.Name.Should().Be("Script script", "an empty display name falls back to the generated name");
    }

    [Fact]
    public void AzureFunctions_AreAvailableInTemplateExpressions()
    {
        var yaml = @"
parameters:
  - name: env
    type: string
    default: staging
  - name: list
    type: object
    default: [a, b, c]
  - name: obj
    type: object
    default: { x: one, y: two }
steps:
  - script: |
      ${{ join(', ', parameters.list) }}|${{ length(parameters.list) }}|${{ upper(parameters.env) }}|${{ lower('ABC') }}
      ${{ format('{0}-{1}', parameters.env, 2) }}|${{ replace('a-b', '-', '_') }}|${{ split('x,y', ',')[1] }}|${{ trim(' t ') }}
      ${{ coalesce(variables.missing, 'fallback') }}|${{ counter('x', 5) }}|${{ containsValue(parameters.obj, 'two') }}|${{ contains('abc', 'B') }}
      ${{ startsWith(parameters.env, 'stag') }}|${{ endsWith(parameters.env, 'ing') }}|${{ in(parameters.env, 'dev', 'staging') }}|${{ notIn(parameters.env, 'dev') }}
      ${{ and(true, ne(parameters.env, 'dev')) }}|${{ or(false, false) }}|${{ not(false) }}|${{ xor(true, false) }}
      ${{ lt(1, 2) }}|${{ le(2, 2) }}|${{ gt(1, 2) }}|${{ ge(3, 2) }}|${{ eq(parameters.env, 'STAGING') }}
      ${{ convertToJson(parameters.list) }}
";

        var lines = Parse(yaml).Jobs["default"].Steps[0].Script!.Split('\n');

        lines[0].Should().Be("a, b, c|3|STAGING|abc");
        lines[1].Should().Be("staging-2|a_b|y|t");
        lines[2].Should().Be("fallback|5|True|True");
        lines[3].Should().Be("True|True|True|True");
        lines[4].Should().Be("True|False|True|True");
        lines[5].Should().Be("True|True|False|True|True");
        string.Join("\n", lines[6..]).Should().Contain("\"a\"").And.Contain("\"c\"");
    }

    #endregion

    #region parameters

    [Fact]
    public void Parameters_MappingForm_InfersTypesFromDefaults()
    {
        var yaml = @"
parameters:
  configuration: Release
  runTests: true
  retries: 2
steps:
  - script: echo ${{ parameters.configuration }} ${{ parameters.retries }}
  - ${{ if eq(parameters.runTests, true) }}:
    - script: echo tests
";

        var steps = Parse(yaml).Jobs["default"].Steps;

        steps.Select(s => s.Script).Should().Equal("echo Release 2", "echo tests");
        Parse(yaml, ("runTests", "false")).Jobs["default"].Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Parameters_CommandLineValues_OverrideDefaultsWithTypeConversion()
    {
        var yaml = @"
parameters:
  - name: timeout
    type: number
    default: 5
  - name: verbose
    type: boolean
    default: false
  - name: regions
    type: object
    default: [eu]
  - name: options
    type: object
    default: {}
jobs:
  - job: Build
    timeoutInMinutes: ${{ parameters.timeout }}
    steps:
      - script: echo ${{ join('+', parameters.regions) }} ${{ parameters.options.retries }}
      - ${{ if parameters.verbose }}:
        - script: echo verbose
";

        var pipeline = Parse(
            yaml,
            ("timeout", "12"),
            ("VERBOSE", "True"),
            ("regions", "[\"eu\", \"us\"]"),
            ("options", "{ retries: 3 }"));

        var job = pipeline.Jobs["Build"];
        job.Timeout.Should().Be(TimeSpan.FromMinutes(12));
        job.Steps.Select(s => s.Script).Should().Equal("echo eu+us 3", "echo verbose");
    }

    [Fact]
    public void Parameters_ValuesRestriction_IsEnforced()
    {
        var yaml = @"
parameters:
  - name: env
    type: string
    default: dev
    values: [dev, staging, prod]
steps:
  - script: echo ${{ parameters.env }}
";

        Parse(yaml, ("env", "prod")).Jobs["default"].Steps[0].Script.Should().Be("echo prod");

        var act = () => Parse(yaml, ("env", "qa"));

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Parameter 'env' value 'qa' is not one of the allowed values: dev, staging, prod.*");
    }

    [Fact]
    public void Parameters_WithoutValueOrDefault_ThrowsNamingTheParameter()
    {
        var yaml = @"
parameters:
  - name: environment
    type: string
steps:
  - script: echo ${{ parameters.environment }}
";

        var act = () => Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().StartWith("Pipeline parameter 'environment' has no value");
        exception.Suggestions.Should().Contain(s => s.Contains("--param environment="));

        Parse(yaml, ("environment", "qa")).Jobs["default"].Steps[0].Script.Should().Be("echo qa");
    }

    [Theory]
    [InlineData("boolean", "yes", "expects a boolean")]
    [InlineData("number", "many", "expects a number")]
    [InlineData("stepList", "just-text", "expects a list")]
    public void Parameters_WithWrongKindOfValue_Throw(string type, string value, string expected)
    {
        var yaml = $@"
parameters:
  - name: p
    type: {type}
steps:
  - script: echo hi
";

        var act = () => Parse(yaml, ("p", value));

        act.Should().Throw<PipelineParseException>().WithMessage($"--param p {expected}*");
    }

    [Fact]
    public void Parameters_UnknownType_Throws()
    {
        var yaml = @"
parameters:
  - name: p
    type: integer
steps:
  - script: echo hi
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Parameter 'p' has unknown type 'integer'*");
    }

    [Fact]
    public void Parameters_UnknownCommandLineName_Warns()
    {
        var yaml = @"
parameters:
  - name: env
    default: dev
steps:
  - script: echo ${{ parameters.env }}
";

        Parse(yaml, ("environment", "prod"));

        Warnings.Should().ContainSingle(w => w.Contains("--param environment") && w.Contains("ignored"));
    }

    [Fact]
    public void ParameterAccess_UndeclaredParameter_ThrowsListingDeclaredOnes()
    {
        var yaml = @"
parameters:
  - name: env
    default: dev
steps:
  - script: echo ${{ parameters.environment }}
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template expression '${{ parameters.environment }}' references parameter 'environment', which is not declared (declared parameters: env)*line 6*");
    }

    #endregion

    #region expression errors

    [Fact]
    public void UnknownFunction_ThrowsWithExpressionTextAndLocation()
    {
        var yaml = @"
steps:
  - script: echo hi
    displayName: ${{ foo(1) }}
";

        var act = () => Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Be("Template expression '${{ foo(1) }}' could not be evaluated: unknown function 'foo()'. (line 4 in pipeline)");
        exception.Context!.LineNumber.Should().Be(4);
        exception.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void RuntimeContext_InTemplateExpression_Throws()
    {
        var yaml = @"
steps:
  - script: echo ${{ dependencies.Build.result }}
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template expression '${{ dependencies.Build.result }}' uses 'dependencies', which is not available when templates are expanded.*");
    }

    [Fact]
    public void StatusFunction_InTemplateExpression_Throws()
    {
        var yaml = @"
steps:
  - ${{ if succeeded() }}:
    - script: echo hi
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("*calls succeeded(), which is only available at run time*");
    }

    [Fact]
    public void MalformedExpression_ThrowsWithLocation()
    {
        var yaml = @"
steps:
  - script: echo ${{ eq(1, }}
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Template expression '${{ eq(1, }}' could not be evaluated: *line 3 in pipeline*");
    }

    [Fact]
    public void UnterminatedExpression_Throws()
    {
        var yaml = @"
steps:
  - script: echo ${{ parameters.x
";

        var act = () => Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Unterminated template expression*");
    }

    #endregion

    #region variables at compile time

    [Fact]
    public void Variables_DefinedEarlier_AreVisibleToLaterTemplateExpressions()
    {
        var yaml = @"
variables:
  a: 1
  b: ${{ variables.a }}2
  c: before-${{ variables.d }}
  d: 4
steps:
  - script: echo ${{ variables.b }} ${{ variables['c'] }}
";

        var pipeline = Parse(yaml);

        pipeline.Variables["b"].Should().Be("12");
        pipeline.Variables["c"].Should().Be("before-", "variables defined later in the file are not visible yet");
        pipeline.Jobs["default"].Steps[0].Script.Should().Be("echo 12 before-");
    }

    [Fact]
    public void Variables_ListFormWithDirectives_AreRegisteredIncrementally()
    {
        var yaml = @"
parameters:
  - name: env
    default: prod
variables:
  - name: base
    value: app
  - ${{ if eq(parameters.env, 'prod') }}:
    - name: suffix
      value: -prod
  - ${{ else }}:
    - name: suffix
      value: -dev
  - name: artifact
    value: ${{ variables.base }}${{ variables.suffix }}
  - group: ignored-group
steps:
  - script: echo $(artifact)
";

        var pipeline = Parse(yaml);

        pipeline.Variables["artifact"].Should().Be("app-prod");
        Parse(yaml, ("env", "dev")).Variables["artifact"].Should().Be("app-dev");
        Warnings.Should().Contain(w => w.Contains("ignored-group"));
    }

    [Fact]
    public void Variables_FromCommandLineAndPredefined_AreAvailable()
    {
        var yaml = @"
steps:
  - script: echo ${{ variables.fromCli }} ${{ variables['Build.Reason'] }} ${{ variables['System.TeamProject'] }} ${{ variables.missing }}
";

        var options = new PipelineParseOptions
        {
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["fromCli"] = "cli-value" },
            EventName = "pull_request"
        };

        _parser.Parse(yaml, options).Jobs["default"].Steps[0].Script.Should().Be("echo cli-value PullRequest local ");
    }

    [Fact]
    public void Variables_OfStagesAndJobs_AreScopedToTheirBlock()
    {
        var yaml = @"
variables:
  root: r
stages:
  - stage: A
    variables:
      stageVar: a
    jobs:
      - job: J
        variables:
          jobVar: j
        steps:
          - script: echo ${{ variables.root }}-${{ variables.stageVar }}-${{ variables.jobVar }}
  - stage: B
    jobs:
      - job: J
        steps:
          - script: echo ${{ variables.root }}-${{ variables.stageVar }}-${{ variables.jobVar }}
";

        var pipeline = Parse(yaml);

        pipeline.Jobs["A_J"].Steps[0].Script.Should().Be("echo r-a-j");
        pipeline.Jobs["B_J"].Steps[0].Script.Should().Be("echo r--");
    }

    [Fact]
    public void Variables_StringBooleanComparison_UsesAzureCoercion()
    {
        var yaml = @"
variables:
  flag: true
steps:
  - ${{ if eq(variables.flag, true) }}:
    - script: echo on
  - ${{ if eq(variables.flag, false) }}:
    - script: echo off
";

        Parse(yaml).Jobs["default"].Steps.Select(s => s.Script).Should().Equal("echo on");
    }

    #endregion

    #region template files

    [Fact]
    public void StepsTemplate_IsSplicedWithParameters_AndPathsResolveRelativeToTheIncludingFile()
    {
        Write("templates/build.yml", @"
parameters:
  - name: configuration
    type: string
  - name: runTests
    type: boolean
    default: true
steps:
  - script: dotnet build -c ${{ parameters.configuration }}
  - ${{ if parameters.runTests }}:
    - template: ./nested/test.yml
      parameters:
        configuration: ${{ parameters.configuration }}
");
        Write("templates/nested/test.yml", @"
parameters:
  - name: configuration
steps:
  - script: dotnet test -c ${{ parameters.configuration }}
");
        Write("pipeline.yml", @"
jobs:
  - job: Build
    steps:
      - script: echo start
      - template: templates/build.yml@self
        parameters:
          configuration: Release
      - template: templates/build.yml
        parameters:
          configuration: Debug
          runTests: false
      - script: echo end
");

        var job = ParseFile("pipeline.yml").Jobs["Build"];

        job.Steps.Select(s => s.Script).Should().Equal(
            "echo start",
            "dotnet build -c Release",
            "dotnet test -c Release",
            "dotnet build -c Debug",
            "echo end");
    }

    [Fact]
    public void JobsAndStagesTemplates_AreSpliced()
    {
        Write("templates/deploy-jobs.yml", @"
parameters:
  - name: environments
    type: object
jobs:
  - ${{ each env in parameters.environments }}:
    - deployment: Deploy_${{ env }}
      environment: ${{ env }}
      strategy:
        runOnce:
          deploy:
            steps:
              - script: echo deploy ${{ env }}
");
        Write("templates/stages.yml", @"
parameters:
  - name: name
stages:
  - stage: ${{ parameters.name }}
    jobs:
      - job: Work
        steps:
          - script: echo ${{ parameters.name }}
");
        Write("pipeline.yml", @"
stages:
  - template: templates/stages.yml
    parameters:
      name: First
  - stage: Deploy
    jobs:
      - template: templates/deploy-jobs.yml
        parameters:
          environments: [dev, prod]
");

        var pipeline = ParseFile("pipeline.yml");

        pipeline.Jobs.Keys.Should().Equal("First_Work", "Deploy_Deploy_dev", "Deploy_Deploy_prod");
        pipeline.Jobs["Deploy_Deploy_prod"].DependsOn.Should().Equal("First_Work");
        pipeline.Jobs["Deploy_Deploy_prod"].Steps[0].Script.Should().Be("echo deploy prod");
    }

    [Fact]
    public void VariablesTemplate_IsSplicedAndVisibleToLaterExpressions()
    {
        Write("vars/common.yml", @"
parameters:
  - name: env
variables:
  environmentName: ${{ parameters.env }}
  ${{ if eq(parameters.env, 'prod') }}:
    logLevel: warning
  ${{ else }}:
    logLevel: debug
");
        Write("pipeline.yml", @"
parameters:
  - name: env
    default: dev
variables:
  - template: vars/common.yml
    parameters:
      env: ${{ parameters.env }}
  - name: artifact
    value: app-${{ variables.environmentName }}-${{ variables.logLevel }}
steps:
  - script: echo $(artifact)
");

        ParseFile("pipeline.yml").Variables["artifact"].Should().Be("app-dev-debug");
        ParseFile("pipeline.yml", ("env", "prod")).Variables["artifact"].Should().Be("app-prod-warning");
    }

    [Fact]
    public void Template_MissingRequiredParameter_ThrowsNamingTemplateAndParameter()
    {
        Write("templates/steps.yml", @"
parameters:
  - name: configuration
    type: string
steps:
  - script: echo ${{ parameters.configuration }}
");
        Write("pipeline.yml", @"
steps:
  - template: templates/steps.yml
");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template 'templates/steps.yml' requires a value for parameter 'configuration'*line 3 in pipeline.yml*");
    }

    [Fact]
    public void Template_UnexpectedParameter_Throws()
    {
        Write("templates/steps.yml", @"
parameters:
  - name: configuration
    default: Release
steps:
  - script: echo ${{ parameters.configuration }}
");
        Write("pipeline.yml", @"
steps:
  - template: templates/steps.yml
    parameters:
      configuraton: Debug
");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template 'templates/steps.yml' does not declare a parameter named 'configuraton' (declared parameters: configuration)*");
    }

    [Fact]
    public void Template_WithoutTheExpectedSection_Throws()
    {
        Write("templates/jobs.yml", @"
jobs:
  - job: A
    steps:
      - script: echo a
");
        Write("pipeline.yml", @"
steps:
  - template: templates/jobs.yml
");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template 'templates/jobs.yml' is referenced from a 'steps' list but does not define a top-level 'steps' section*");
    }

    [Fact]
    public void TemplateReference_WithExtraKeys_Throws()
    {
        Write("templates/steps.yml", "steps:\n  - script: echo a\n");
        Write("pipeline.yml", @"
steps:
  - template: templates/steps.yml
    displayName: not allowed
");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>().WithMessage("A template reference may only contain 'template' and 'parameters'*'displayName'*");
    }

    [Fact]
    public void ExpressionErrorInsideTemplate_PointsAtTheTemplateFileAndTheIncludingLine()
    {
        Write("templates/steps.yml", @"
steps:
  - script: echo ${{ parameters.missing }}
");
        Write("pipeline.yml", @"
jobs:
  - job: Build
    steps:
      - template: templates/steps.yml
");

        var act = () => ParseFile("pipeline.yml");

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("parameter 'missing'");
        exception.Message.Should().Contain("(line 3 in templates/steps.yml, included from pipeline.yml line 5)");
        exception.Context!.PipelineFile.Should().EndWith("steps.yml");
        exception.Context.LineNumber.Should().Be(3);
    }

    [Fact]
    public void StructureErrorInsideTemplate_PointsAtTheTemplateFile()
    {
        Write("templates/steps.yml", @"
steps:
  - script: echo hi
    timeoutInMinutes: soon
");
        Write("pipeline.yml", @"
steps:
  - template: templates/steps.yml
");

        var act = () => ParseFile("pipeline.yml");

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("steps.yml at line 4");
        exception.Message.Should().Contain("timeoutInMinutes");
        exception.Context!.PipelineFile.Should().EndWith("steps.yml");
    }

    [Fact]
    public void YamlSyntaxErrorInsideTemplate_PointsAtTheTemplateFile()
    {
        Write("templates/steps.yml", "steps:\n  - script: echo hi\n   badly: indented\n");
        Write("pipeline.yml", "steps:\n  - template: templates/steps.yml\n");

        var act = () => ParseFile("pipeline.yml");

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().StartWith("Invalid YAML syntax in steps.yml");
        exception.Context!.PipelineFile.Should().EndWith("steps.yml");
    }

    [Fact]
    public void IncludeCycle_ThrowsWithTheChain()
    {
        Write("templates/a.yml", "steps:\n  - template: b.yml\n");
        Write("templates/b.yml", "steps:\n  - template: a.yml\n");
        Write("pipeline.yml", "steps:\n  - template: templates/a.yml\n");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template include cycle detected: templates/a.yml -> templates/b.yml -> templates/a.yml.*");
    }

    [Fact]
    public void IncludeDepth_IsLimited()
    {
        for (var i = 0; i < AzureTemplateProcessor.MaxIncludeDepth + 2; i++)
        {
            Write($"t{i}.yml", $"steps:\n  - template: t{i + 1}.yml\n");
        }

        Write("pipeline.yml", "steps:\n  - template: t0.yml\n");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>().WithMessage($"Templates are nested more than {AzureTemplateProcessor.MaxIncludeDepth} levels deep*");
    }

    [Fact]
    public void TemplateFromAnotherRepository_ThrowsWithVendoringSuggestion()
    {
        Write("pipeline.yml", @"
steps:
  - template: steps/build.yml@shared
");

        var act = () => ParseFile("pipeline.yml");

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("Template 'steps/build.yml@shared' refers to repository resource 'shared': templates from other repositories are not supported");
        exception.Suggestions.Should().Contain(s => s.Contains("vendor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorkspaceRootedTemplatePath_ResolvesAgainstTheWorkspace()
    {
        Write("shared/steps.yml", "steps:\n  - script: echo shared\n");
        Write("pipelines/pipeline.yml", "steps:\n  - template: /shared/steps.yml\n");

        var options = new PipelineParseOptions { WorkspacePath = _root };
        var pipeline = _parser.ParseFile(Path.Combine(_root, "pipelines", "pipeline.yml"), options).GetAwaiter().GetResult();

        pipeline.Jobs["default"].Steps[0].Script.Should().Be("echo shared");
    }

    #endregion

    #region extends

    [Fact]
    public void Extends_UsesTheTemplateAsThePipeline_AndMergesVariables()
    {
        Write("templates/pipeline.yml", @"
parameters:
  - name: projects
    type: object
  - name: publish
    type: boolean
    default: false
variables:
  fromTemplate: t
  shared: template-value
stages:
  - stage: Build
    jobs:
      - ${{ each project in parameters.projects }}:
        - job: Build_${{ project }}
          steps:
            - script: echo ${{ project }} ${{ variables.fromRoot }}
  - ${{ if parameters.publish }}:
    - stage: Publish
      jobs:
        - job: Publish
          steps:
            - script: echo publish
");
        Write("pipeline.yml", @"
name: Extending
trigger: none
parameters:
  - name: publish
    type: boolean
    default: false
variables:
  fromRoot: r
  shared: root-value
extends:
  template: templates/pipeline.yml
  parameters:
    projects: [Api, Worker]
    publish: ${{ parameters.publish }}
");

        var pipeline = ParseFile("pipeline.yml");

        pipeline.Name.Should().Be("Extending");
        pipeline.Jobs.Keys.Should().Equal("Build_Build_Api", "Build_Build_Worker");
        pipeline.Jobs["Build_Build_Api"].Steps[0].Script.Should().Be("echo Api r");
        pipeline.Variables.Should().Equal(new Dictionary<string, string>
        {
            ["fromTemplate"] = "t",
            ["shared"] = "root-value",
            ["fromRoot"] = "r"
        });

        ParseFile("pipeline.yml", ("publish", "true")).Jobs.Keys.Should().Equal("Build_Build_Api", "Build_Build_Worker", "Publish_Publish");
    }

    [Fact]
    public void Extends_WithStagesInTheExtendingFile_Throws()
    {
        Write("templates/pipeline.yml", "steps:\n  - script: echo hi\n");
        Write("pipeline.yml", @"
extends:
  template: templates/pipeline.yml
stages:
  - stage: A
    jobs: []
");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>().WithMessage("A pipeline that uses 'extends' cannot also define 'stages'*");
    }

    [Fact]
    public void Extends_NestedExtends_Throws()
    {
        Write("templates/outer.yml", "extends:\n  template: inner.yml\n");
        Write("templates/inner.yml", "steps:\n  - script: echo hi\n");
        Write("pipeline.yml", "extends:\n  template: templates/outer.yml\n");

        var act = () => ParseFile("pipeline.yml");

        act.Should().Throw<PipelineParseException>().WithMessage("*uses 'extends' itself*");
    }

    #endregion

    #region processor API

    [Fact]
    public void Processor_WithInMemoryFiles_ExposesExpandedYamlAndSources()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Path.GetFullPath(Path.Combine(_root, "templates", "steps.yml"))] = "parameters:\n  - name: text\nsteps:\n  - script: echo ${{ parameters.text }}\n"
        };

        var warnings = new List<string>();
        var processor = new AzureTemplateProcessor(
            new PipelineParseOptions { WorkspacePath = _root, Parameters = new Dictionary<string, string> { ["greeting"] = "hello" } },
            warnings,
            path => files.TryGetValue(path, out var content) ? content : null);

        var result = processor.Process(
            "parameters:\n  - name: greeting\nsteps:\n  - template: templates/steps.yml\n    parameters:\n      text: ${{ parameters.greeting }} world\n",
            null);

        result.RootFile.Should().Be("pipeline");
        result.Sources.Keys.Should().HaveCount(2);
        result.ToYaml().Should().Contain("echo hello world").And.NotContain("${{");
        result.Origins.Should().NotBeEmpty();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Processor_RemovesParametersAndLeavesNoTemplateExpressions()
    {
        var processor = new AzureTemplateProcessor(PipelineParseOptions.None);

        var result = processor.Process("parameters:\n  - name: x\n    default: 1\nsteps:\n  - script: echo ${{ parameters.x }}\n", null);

        var yaml = result.ToYaml();
        yaml.Should().NotContain("parameters").And.NotContain("${{");
        yaml.Should().Contain("echo 1");
    }

    #endregion
}
