using MCP_MI.Samples;
using MCP_MI;

try
{
    //await IcmMcpClientSample.RunAsync();
    await IcmMcpClientSample.RunInteractiveAsync();
}
catch (Exception ex)
{
    Console.WriteLine("RUN_FAILED");
    Console.WriteLine(ex.Message);
}
