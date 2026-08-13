using AIPMS.Api.Middleware;

namespace AIPMS.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors("Frontend");
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
