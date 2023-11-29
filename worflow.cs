using Core.Constants.Messages;
using Core.Model;
using Dapr.Workflow;
using Parking.API.Workflows.Activities;
using Parking.API.Workflows.Models;

namespace Parking.API.Workflows.Workflows
{
    public class ResidentialValetParkingWorkflow : Workflow<ParkingRequest, ParkingRequestResult>
    {
        public override async Task<ParkingRequestResult> RunAsync(WorkflowContext context, ParkingRequest input)
        {
            var retryOptions = new WorkflowTaskOptions
            {
                RetryPolicy = new WorkflowRetryPolicy(
                firstRetryInterval: TimeSpan.FromMinutes(1),
                backoffCoefficient: 2.0,
                maxRetryInterval: TimeSpan.FromHours(1),
                maxNumberOfAttempts: 10),
            };
            var ReceiveParkingRequestActivityResult= await context.CallActivityAsync<WorkflowActivityResult>(nameof(NotifyNewRequestActivity), input);
            if (ReceiveParkingRequestActivityResult.Status.Equals(STATUS.Failed))
            {
                return new ParkingRequestResult
                (
                   STATUS.Failed,
                   string.Format(ParkingExceptionMessage.WORKFLOW_ERROR,GetType().Name,nameof(NotifyNewRequestActivity),ReceiveParkingRequestActivityResult.Status.ToString())
                
                );
            }
            
            //wait for service provider to confirm service request
            var confirmationResult = await context.WaitForExternalEventAsync<WorkflowActivityResult>(
                eventName: "ParkingServiceRequestConfirmedIntegrationEvent",
                timeout: TimeSpan.FromMinutes(30));

            return null;
        }
    }
}
