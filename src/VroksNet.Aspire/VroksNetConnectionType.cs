namespace Aspire.Hosting;

/// <summary>
/// The kind of a VroksNet connection — what VroksNet sends to, listens on or runs contract tests
/// against. Passed to the container as <c>Provisioning__Connections__&lt;i&gt;__Type</c> (image contract §5).
/// </summary>
public enum VroksNetConnectionType
{
    /// <summary>An HTTP service; the value is its base URL.</summary>
    Http,

    /// <summary>A RabbitMQ broker (AMQP URI).</summary>
    RabbitMq,

    /// <summary>A NATS server.</summary>
    Nats,

    /// <summary>A Kafka cluster (bootstrap servers).</summary>
    Kafka,

    /// <summary>An MQTT broker.</summary>
    Mqtt,

    /// <summary>A Redis server (Pub/Sub or streams).</summary>
    Redis,

    /// <summary>Azure Service Bus (or its emulator).</summary>
    ServiceBus,

    /// <summary>AWS SQS.</summary>
    Sqs,

    /// <summary>AWS SNS.</summary>
    Sns,
}
