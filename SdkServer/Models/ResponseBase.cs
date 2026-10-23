namespace MikuSB.SdkServer.Models;

public class ResponseBase
{
    public string Msg { get; set; } = "OK";
    public bool Success { get; set; } = true;
    public int Code { get; set; }
    public object? Data { get; set; }
}
