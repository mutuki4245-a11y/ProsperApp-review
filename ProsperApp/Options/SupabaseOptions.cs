namespace ProsperApp.Options;

public class SupabaseOptions
{
    public string Url { get; set; } = string.Empty;
    public string RpcEdgeFunctionUrl { get; set; } = string.Empty;
    public string RpcEdgeFunctionKey { get; set; } = string.Empty;
    public string RpcProxyFunctionName { get; set; } = "prosper-rpc";
}
