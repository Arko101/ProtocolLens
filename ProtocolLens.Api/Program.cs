using ProtocolLens.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
//Adding CORS policy to allow requests from Angular frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
        );
});

var app = builder.Build();
app.UseCors("AngularPolicy");


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



app.MapGet("/api/status", () =>
{
    return new
    {
        message = "ProtocolLens API is running"
    };
});

app.MapPost("/api/pcap", async (IFormFile file) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "No file provided" });
    }
    string tmpFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try
    {
        var fileName = file.FileName;
        var fileSize = file.Length;
        var pcapReader = new PcapReader();

        using (var tempStream = new FileStream(tmpFilePath, FileMode.Create))
        {
            await file.CopyToAsync(tempStream);
        }
        var dhcpPackets = pcapReader.ExtractDhcpPacketsFromPcapFile(tmpFilePath);

        return Results.Ok(
          new
          {
              fileName = fileName,
              fileSize = fileSize,
              dhcpPackets
          }

          );
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500
        );
    }
    finally
    {
        if(File.Exists(tmpFilePath))
        {
            File.Delete(tmpFilePath);
        }
    }
}).DisableAntiforgery();
app.Run();


