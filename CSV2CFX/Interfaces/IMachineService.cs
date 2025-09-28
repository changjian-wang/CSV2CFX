using CSV2CFX.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Interfaces
{
    public interface IMachineService
    {
        Task CreateRabbitmqAsync(string uniqueId);

        /// <summary>
        /// Publishes a heartbeat message to a RabbitMQ exchange, indicating the current status of the machine.
        /// </summary>
        /// <remarks>This method constructs a heartbeat message containing metadata about the machine,
        /// such as its location, name, and process type, along with other status information. The message is serialized
        /// to JSON and published to a RabbitMQ exchange. The method also includes a delay based on the configured
        /// heartbeat frequency to ensure periodic message publishing.</remarks>
        /// <param name="uniqueId">A unique identifier for the machine, used to construct the RabbitMQ exchange, queue, and routing key names.</param>
        /// <returns></returns>
        Task PublishHeartbeatAsync(string uniqueId);

        /// <summary>
        /// Publishes a "Work Started" message for each production record in the specified CSV file.
        /// </summary>
        /// <remarks>This method reads production data from a CSV file located in the configured process
        /// data folder. If the file does not exist or the folder path is invalid, the method exits without performing
        /// any action.  For each production record in the file, the method constructs a message containing production
        /// details and metadata, and publishes it to a RabbitMQ exchange. The method ensures that the original file is
        /// backed up and deleted after processing.  Exceptions during message publishing are logged, and the file is
        /// deleted in the <c>finally</c> block to ensure cleanup.</remarks>
        /// <param name="uniqueId">A unique identifier used to construct the RabbitMQ exchange, queue, and routing key names.</param>
        /// <returns></returns>
        Task PublishWorkProcessAsync(string uniqueId);

        /// <summary>
        /// Publishes the current state of the machine identified by the specified unique ID.
        /// </summary>
        /// <param name="uniqueId">The unique identifier of the machine whose state is to be published. Cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task PublishMachineStateAsync(string uniqueId);
    }
}
