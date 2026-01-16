
namespace Quartz.Store.MongoDb.Examples
{
    internal class HelloJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine("Hi, this is HelloJob!");
            await Task.CompletedTask;
        }
    }
}
