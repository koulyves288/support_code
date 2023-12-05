using Core.Constants.Messages;
using Core.Constants.Values;
using Core.Exceptions;
using Dapr.Client;
using Dapr.Workflow;
using EventBus.Abstractions;
using EventBus.Events.Parking;
using Logger.Interface;
using Microsoft.OpenApi.Extensions;
using Parking.API.Enums;
using Parking.API.Models.Entities;
using Parking.API.Repositories;
using Parking.API.Workflows.Activities;
using Parking.API.Workflows.Workflows;
using System.Data.Entity.Infrastructure.Design;

namespace Parking.API.IntegrationEvents.Handling
{
    public class ParkingServiceRequestSubmittedIntegrationEventHandler : IIntegrationEventHandler<ParkingServiceRequestSubmittedIntegrationEvent>
    {
        
        private readonly ISystemLoggerService _logger;
        private readonly IRepository _repository;
       
        public ParkingServiceRequestSubmittedIntegrationEventHandler(
          
            ISystemLoggerService logger,
            IRepository repository
         )
        {
          

            _logger = logger;
            _repository = repository;
            
        }

        public async Task Handle(ParkingServiceRequestSubmittedIntegrationEvent @event)
        {
            string workflowId = Guid.NewGuid().ToString()[..8];
            try
            {
               
                    var builder=Host.CreateDefaultBuilder().ConfigureServices(services =>
                    {
                        services.AddDaprWorkflow(options =>
                        {
                        
                             // Note that it's also possible to register a lambda function as the workflow
                             // or activity implementation instead of a class.
                             options.RegisterWorkflow<ResidentialValetParkingWorkflow>();
                             // These are the activities that get invoked by the workflow(s).
                             options.RegisterActivity<NotifyNewRequestActivity>();
                             options.RegisterActivity<NotifyParkingServiceConfirmationActivity>();

                         });
                     });
                using var host = builder.Build();
                host.Start();
                var subservicename = @event.SubServiceName;
                    using var daprClient = ProgramExtension.GetDaprClient();
                
                    if (subservicename.Replace(" ", "").ToUpper().Equals(ParkingType.ValetParking.ToString().ToUpper()))
                    {
                        await daprClient.WaitForSidecarAsync();
                        await _logger.Log(string.Format(ParkingInformationMessage.STARTING_WORKFLOW, workflowId, @event.ServiceRequestId, @event.SubServiceName), Values.PARKING_APP_NAME, Values.INFORMATION_LOGGER_LEVEL, GetType().Name);
                        var parkingworkflow = new ParkingServiceWorkflowEntity
                        {
                            ServiceRequestId = @event.ServiceRequestId,
                            DaprWorkflowInstanceId = workflowId,
                            Status = "submitted",
                            AddedBy = new Guid(Values.SYSTEM_ID),
                            AddedDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                        };
                        await _repository.RecordServiceRequestWorkflowAsync(parkingworkflow);
                       var startworkflowresponse= await daprClient.StartWorkflowAsync(
                        workflowComponent: Values.DaprWorkflowComponent,
                        workflowName: nameof(ResidentialValetParkingWorkflow),
                        input: @event,
                        instanceId: workflowId);

                        // Wait for the workflow to start and confirm the input
                        GetWorkflowResponse state = await daprClient.WaitForWorkflowStartAsync(
                            instanceId: workflowId,
                            workflowComponent: Values.DaprWorkflowComponent);
                        if (state.RuntimeStatus.Equals(WorkflowRuntimeStatus.Running))
                        {

                            await _logger.Log(string.Format(ParkingInformationMessage.WORKFLOW_STARTED, state.InstanceId), Values.PARKING_APP_NAME, Values.INFORMATION_LOGGER_LEVEL, GetType().Name);
                        };


                        // Wait for the workflow to complete
                        state = await daprClient.WaitForWorkflowCompletionAsync(
                            instanceId: workflowId,
                            workflowComponent: Values.DaprWorkflowComponent);
                        if (state.RuntimeStatus.Equals(WorkflowRuntimeStatus.Completed))
                        {
                            await _logger.Log(string.Format(ParkingInformationMessage.WORKFLOW_COMPLETED, state.InstanceId), Values.PARKING_APP_NAME, Values.INFORMATION_LOGGER_LEVEL, GetType().Name);
                        }
                    }
                
               
                

            }
            catch (Exception ex)
            {
               
                await _logger.Log(string.Format(ParkingExceptionMessage.HANDLING_EVENT_ERROR, @event.GetType().ToString()), Values.PARKING_APP_NAME, Values.ERROR_LOGGER_LEVEL, GetType().Name, ex);
                await _logger.Log(string.Format(ParkingExceptionMessage.WORKFLOW_ERROR, workflowId), Values.PARKING_APP_NAME, Values.ERROR_LOGGER_LEVEL, GetType().Name, ex);
            }
        }

    
    }

}
