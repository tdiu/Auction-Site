namespace API.Interfaces;

public interface IEmailTemplateRenderer
{
    Task<string> RenderAsync(string templateName, object model, CancellationToken ct);
}
