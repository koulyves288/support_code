using Core.Constants.Messages;
using Core.Constants.Values;
using Dapr.Client;
using Dapr.Workflow;
using EventBus.Events.Parking;
using Google.Api;
using Microsoft.Extensions.Logging;
using Parking.API;
using Parking.API.Workflows.Activities;
using Parking.API.Workflows.Workflows;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddCustomConfiguration();
//builder.AddCustomDatabase();
builder.AddCustomApplicationServices();
builder.AddCustomMvc();
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var Wbuilder = Host.CreateDefaultBuilder().ConfigureServices(services =>
{
    services.AddDaprWorkflow(options =>
    {
        // Note that it's also possible to register a lambda function as the workflow
        // or activity implementation instead of a class.
        options.RegisterWorkflow<ResidentialValetParkingWorkflow>();

        // These are the activities that get invoked by the workflow(s).
        options.RegisterActivity<NotifyNewRequestActivity>();

    });
});
string workflowId = Guid.NewGuid().ToString()[..8];
using var host = Wbuilder.Build();
host.Start();
using var daprClient = new DaprClientBuilder().Build();
await daprClient.WaitForSidecarAsync();
var @event = new ParkingServiceRequestSubmittedIntegrationEvent(
    new Guid("48B5090D-22A2-49CD-8D9B-08DBEECE4DD6"), 
    new Guid("C4A63BA7-0737-4C01-DC19-08DBDFDA63EF"), 
    new Guid("C4A63BA7-0737-4C01-DC19-08DBDFDA63EF"), 
    "{}", 
    "Valet Parking");
await daprClient.StartWorkflowAsync(
workflowComponent: Values.DaprWorkflowComponent,
 workflowName: nameof(ResidentialValetParkingWorkflow),
 input: @event,
 instanceId: workflowId);

// Wait for the workflow to start and confirm the input
GetWorkflowResponse state = await daprClient.WaitForWorkflowStartAsync(
    instanceId: workflowId,
    workflowComponent: Values.DaprWorkflowComponent);

var app = builder.Build();
app.UseRouting();

var pathBase = builder.Configuration["PATH_BASE"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
}
app.UseStaticFiles();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseAuthorization();
app.UseCloudEvents();
app.MapControllers();
app.MapDefaultControllerRoute();
app.MapSubscribeHandler();
try
{
    app.Logger.LogInformation("Seeding database ({ApplicationName})...", Values.PARKING_APP_NAME);

    // Apply database migration automatically. Note that this approach is not
    // recommended for production scenarios. Consider generating SQL scripts from
    // migrations instead.
    //using (var scope = app.Services.CreateScope())
    //{
    //    await SeedData.EnsureSeedData(scope, app.Configuration, app.Logger);
    //}

    app.Logger.LogInformation("Starting web host ({ApplicationName})...", Values.PARKING_APP_NAME);
    app.Run();

    return 0;
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex, "Host terminated unexpectedly ({ApplicationName})...", Values.PARKING_APP_NAME);
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

