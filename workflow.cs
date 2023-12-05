using Core.Constants.Messages;
using Core.Model;
using Dapr.Client;
using Dapr.Workflow;
using Parking.API.Data;
using Parking.API.Repositories;
using Parking.API.Workflows.Activities;
using Parking.API.Workflows.Models;

namespace Parking.API.Workflows.Workflows
{
    public class ResidentialValetParkingWorkflow : Workflow<ParkingRequest, ParkingRequestResult>
    {
        private readonly Repository _repository=new Repository(new ApplicationDbContext());
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
            context.SetCustomStatus("received");
            await  _repository.UpdateServiceRequestStatus(input.ServiceRequestId, "received");
            var NotificationForNewParkingServiceSubmitted= await context.CallActivityAsync<WorkflowActivityResult>(nameof(NotifyNewRequestActivity), input, retryOptions);
            if (NotificationForNewParkingServiceSubmitted.Status.ToString().Equals(STATUS.Failed.ToString().ToLowerInvariant()))
            {
                return new ParkingRequestResult
                (
                   STATUS.Failed,
                   string.Format(ParkingExceptionMessage.WORKFLOW_ERROR,GetType().Name,nameof(NotifyNewRequestActivity), NotificationForNewParkingServiceSubmitted.Status.ToString())
                
                );
            }
            context.SetCustomStatus("waiting for confirmation");
            //wait for service provider to confirm service request
            var confirmationResult = await context.WaitForExternalEventAsync<WorkflowActivityResult>(
                eventName: "ParkingServiceRequestConfirmed",
                timeout: TimeSpan.FromMinutes(30));
            if(confirmationResult.Status.ToString().Equals(STATUS.Successful.ToString().ToLowerInvariant()))
            {
               
                try
                {
                    context.SetCustomStatus("confirmed");
                    await _repository.UpdateServiceRequestStatus(input.ServiceRequestId, "confirmed");
                   
                    //Console.Write(context.)
                    var result = await context.CallActivityAsync<WorkflowActivityResult>(nameof(NotifyParkingServiceConfirmationActivity), input, retryOptions);
                    return new ParkingRequestResult(STATUS.Processed, "Service request has been processed");
                }
                catch(Exception ex)
                {
                    return new ParkingRequestResult(STATUS.NotProcessed, "Service request has not been processed");
                }
            }
            
            
            //if (NotificationForNewParkingServiceConfirmationSubmitted.Status.ToString().Equals(STATUS.Failed.ToString().ToLowerInvariant()))
            //{
            //    return new ParkingRequestResult
            //    (
            //       STATUS.Failed,
            //       string.Format(ParkingExceptionMessage.WORKFLOW_ERROR, GetType().Name, nameof(NotifyParkingServiceConfirmationActivity), NotificationForNewParkingServiceConfirmationSubmitted.Status.ToString())

            //    );
            //}

            return new ParkingRequestResult(STATUS.Rejected,"Service request has been rejected");
        }
    }
}
