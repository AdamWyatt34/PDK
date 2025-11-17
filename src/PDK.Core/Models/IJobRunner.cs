namespace PDK.Core.Models;

public interface IJobRunner
{
    Task<JobResult> RunJob(Job job, RunContext context);
    Task<StepResult> RunStep(Step step, RunContext context);
}