# Standard RabbitMQ message flow
<br />
1.The producer publishes a message to the exchange.<br /><br />
2.The exchange receives the message and is now responsible for the routing of the message.<br /><br />
3.Binding must be set up between the queue and the exchange. In this case, we have bindings to two different queues from the exchange. The exchange routes the message into the queues.<br /><br />
4.The messages stay in the queue until they are handled by a consumer.<br /><br />
5.The consumer handles the message.<br />


![RabbitMq](../images/rabbitMqFlow.png)

<br />

# Run RabbitMQ server on Docker

<br />


In order to run the RabbitMQ server via Docker, you must have Docker installed on your computer.<br />
You can download it from the following : <https://www.docker.com/products/docker-desktop/><br /><br />
To download and run RabbitMQ in Docker, you need to enter the following command in command prompt:<br />
**docker run -d -it --hostname localhost -v C:\rabbitmq-data:/var/lib/rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management**<br /><br />
Docker command parameters:<br /><br />
**docker run**: This is the command to run a new container.<br />
**--d**: This option specifies that the container should run in detached mode, which allows the container to run in the background.<br />
**-it**: This specifies that this container should run in interactive mode, allowing you to see the logs, and providing the ability to stop the container.<br />
**-hostname localhost**: This option allows you to specify a custom hostname for the container, in this case it's set to "localhost"<br />
**-v C:\rabbitmq-data:/var/lib/rabbitmq**: This option mounts the local folder C:\rabbitmq-data on the host machine to the /var/lib/rabbitmq directory within the container. This will ensure that any data stored in the /var/lib/rabbitmq directory within the container will be stored in the local folder C:\rabbitmq-data, and will persist even if the container is deleted.It's important to note that you should have the necessary permissions to access the C:\rabbitmq-data folder on the host machine and the /var/lib/rabbitmq folder within the container, and you should make sure that the folder exists before running the command.<br />
**-p 5672:5672 -p 15672:15672**: This option maps the host ports to the container ports. The **-p** flag is used for port mapping, it takes two arguments. The first argument is the host port and the second argument is the container port.
In this example, it maps the host port 5672 to container port 5672, and host port 15672 to container port 15672. This will expose ports 5672 and 15672 on the host, allowing you to access the RabbitMQ service and the management console respectively.<br />
**rabbitmq:3-management**: This is the image name, version, and tag (with management plugin enabled) that you want to run in the container.<br />

With this command you will be able to access the management console by going to http://localhost:15672 in this case <br /><br />


# Implement RabbitMq in .Net
<br/>

## 1.RabbitMQ Client<br/><br />
You need to install the RabbitMQ client nuget package : <https://www.nuget.org/packages/RabbitMQ.Client>
<br/><br/>

## 2.Connect to RabbitMQ Server<br/><br />
You must define connection string.<br />
```csharp
        "ConnectionStrings": {

            "RabbitMqConnection": "amqp://{username}:{password}@{server}/{vhost}"

        }
```
Connection string parameters:<br/>

**amqp://**: This is the protocol for connecting to the RabbitMQ broker. In this case, it's AMQP, which is the default protocol for connecting to RabbitMQ.

**{username}**: This is the username used to authenticate to the RabbitMQ broker. Replace this with the actual username for your installation.

**{password}**: This is the password used to authenticate to the RabbitMQ broker. Replace this with the actual password for your installation.

**{server}**: This is the hostname or IP address of the RabbitMQ broker. Replace this with the actual hostname or IP address of your installation.

**/{vhost}**: This is the virtual host to connect to
<br /><br/>
Default values for connection string:<br/>
```csharp
        "ConnectionStrings": {

            "RabbitMqConnection": "amqp://guest:guest@localhost:5672/"

        }
```
After that you need to implement the following code for the connection:


```csharp
        ConnectionFactory connectionFactory = new ConnectionFactory
            {
                Uri = new Uri(configuration["ConnectionStrings:RabbitMqConnection"]),
                AutomaticRecoveryEnabled = true,
                DispatchConsumersAsync = true
            };

        IConnection connection = connectionFactory.CreateConnection();
```
**ConnectionFactory** class is responsible for creating new connections to a RabbitMQ server.

**AutomaticRecoveryEnabled = true**: This line enables the automatic recovery feature of the connection factory. When automatic recovery is enabled, the connection factory will automatically attempt to recover connections that have been closed unexpectedly.

**DispatchConsumersAsync = true**: This line enables the asynchronous dispatching of messages to consumers. This means that the messages will be delivered to the consumers on a separate thread, allowing the connection to handle multiple messages at the same time.

**CreateConnection()** method of the connection factory is called, which creates a new connection to the RabbitMQ server specified in the URI and returns it.
<br/><br/>

## 3.Create channel and configure queue<br/><br />
In RabbitMQ, a channel is a virtual connection within a physical connection. 
Channels are used to send and receive messages, publish and consume messages, create and delete queues, and perform other operations on the broker.

The Connection.CreateModel() method creates a new channel on the connection and returns it as an instance of the IModel interface.<br/><br/>
**serverConnection** represents the connection to the server that we explained earlier<br />
It is important to say that the channel must be created within the **using** if we want to delete the channel after it is no longer used.
```csharp
        using var channel = serverConnection.CreateModel();
            
        channel.ExchangeDeclare(deadLetterExchangeName, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.QueueDeclare(deadLetterQueueName, durable: true, exclusive: false, autoDelete: false, null);
        channel.QueueBind(deadLetterQueueName, deadLetterExchangeName, deadLetterRoutingKeyName);

        channel.ExchangeDeclare(exchangeName, ExchangeType.Direct, durable: true, autoDelete: false);
        channel.QueueDeclare(
            queueName, 
            durable: true, 
            exclusive: false, 
            autoDelete: false,
            new Dictionary<string, object>
            {
            { "x-dead-letter-exchange", deadLetterExchangeName },
            { "x-dead-letter-routing-key", deadLetterRoutingKeyName}
            });
        channel.QueueBind(queueName, exchangeName, routingKeyName, null);
            
```

**channel.ExchangeDeclare** is a method used to declare an exchange on the RabbitMQ. An exchange is a messaging entity that routes messages to queues based on a routing key. Exchanges can be of different types (e.g. direct, fanout, topic, headers) which determine how messages are routed to queues.

**channel.QueueDeclare** is a method used to declare a new queue on the RabbitMQ. A queue is a messaging entity that holds messages that are waiting to be consumed.

**channel.QueueBind** is a method used to bind a queue to an exchange, which enables the exchange to route messages to the queue based on a routing key. The routing key is a string that is used to determine which messages should be sent to the queue.

Declare parameters:

**durable**: If a queue is durable, it means that it will survive a  (message queue server) restart.

**exclusive**: An exclusive queue is only accessible by the connection that declared it.

**autodelete**: An autodelete queue will be deleted by the broker automatically when the last consumer unsubscribes.

**arguments**: The arguments are additional options that might be specific to the message broker you are using.In our example we define dead letter in arguments.

In the given example, we have configured two queues, one queue is a dead letter queue that represents the queue to which failed messages from the main queue are automatically sent, the second queue is the main queue and in its arguments it is defined to which dead letter queue the failed messages will be sent.

It is important to note that when we declare a queue or exchange, if the queue already exists and has the same name, properties, and options as the queue you are trying to declare, then the existing queue will be used and no new queue will be created, the same rule applies to exchanges.

## 4.Send messages to queue<br/><br />

**BasicPublish** method is used to publish a message to the message broker.<br />
**properties.DeliveryMode = 2**: This line sets the DeliveryMode property of the message to 2, which indicates that the message should be persisted to disk.

It is important to note that before sending a message to the queue, the connection to the server must be established and the queue and exchange previously defined.

```csharp
        using var channel = serverConnection.CreateModel();

        var properties = channel.CreateBasicProperties();
        properties.DeliveryMode = 2;

        channel.BasicPublish(
            exchangeName,
            routingKeyName,
            properties,
            serializedMessage);
```

## 5.Recieve messages from queue<br/><br />

**BasicConsume** is a method used to consume messages from a specified queue asynchronously. The method takes three arguments:

**queue**: The name of the queue from which messages should be consumed.

**autoAck**: If this parameter is set to true, the server will automatically acknowledge messages as they are delivered to the consumer. If it is set to false, the consumer must manually acknowledge the messages using the IModel.BasicAck method.

**consumer**: The EventingBasicConsumer or AsyncEventingBasicConsumer instance that will handle the messages as they are received.

```csharp
            Imodel emailChannel = serverConnection.CreateModel(;

            this.emailChannel.BasicQos(0, 1, false);

            AsyncEventingBasicConsumer eventingBasicConsumer = new(this.emailChannel);

            eventingBasicConsumer.Received += async(sender, ea) => {
                try
                {
                    await this.emailService.SendEmail(email);

                    this.emailChannel.BasicAck(e.DeliveryTag, false);
                }
                catch (Exception)
                {
                    this.emailChannel.BasicNack(e.DeliveryTag, false, false);
                }
            };

            this.emailChannel.BasicConsume(this.queueName, false, eventingBasicConsumer);
        
```

**BasicQos** is method witch controls the number of unacknowledged messages that can be sent to a consumer before the broker stops sending messages. For example, if you set the prefetchCount to 1, the broker will only send one message to the consumer at a time, and will not send any more messages until the consumer acknowledges the previous message.

**BasicAck** and **BasicNack** are methods , which are used to acknowledge or negatively acknowledge a message that has been delivered to a consumer.

If the message is acknowledged it will be removed from the queue, if not, it will be sent to the dead letter queue.

In this example, if the email service has successfully sent the email message, it will be removed from the queue, if it is not sent successfully, it will be sent to the dead letter queue.<br/><br/><br/>

# RabbitMQ in our Template project explanation

1.To use RabbitMQ in our application you must configure RabbitMq in appsettings.json, you need to set 'IsRabbitMqEnabled' parameter to true and addd queue in Queues list.

2.After configuring RabbitMQ, to send message to queue you need to inject IRabbitMqQueueService and call the Enqueu method.In our example IRabbitMqQueueService is implemented by RabbitMqQueueService.

3.The advice is to inject the IRabbitMqQueueService in one class and to get the QueueCongif object from the configuration there, and wherever you use that specific queue to send message on queue, call that service. But you can also inject the service each time separately and take the QueueConfig object from configuration each time.In the given example, the email service injects IRabbitMqQueueService and all other services that send email call it and send only message like parameter.

4.To receive a message from queue, it is necessary to create a class that inherits BaseConsumer abstract class and implements a OnReceivedMessage abstract method. The implementation of the OnReceivedMessage method should be the logic of processing the received message.

5.The name of the class that implements the BaseConsumer class must be added to the RabbitMq config in appsettings.json(RabbitMQ:Queues:InitClassName)




```csharp
        "RabbitMq": {
        "IsRabbitMqEnabled": true,
        "Queues": [
            {
                "Name": "Email",
                "UseSingleMessageRecieve": false,
                "NumberOfConsumers": 0,
                "MainQueue": {
                    "QueueName": "email-send",
                    "ExchangeName": "email-box",
                    "RoutingKeyName": "email-add",
                    "InitClassName": "Template.Logic.RabbitMqConsumers.EmailConsumer"
                },
                "DeadLetterQueue": {
                    "QueueName": "dead-letter-email-send",
                    "ExchangeName": "dead-letter-email-box",
                    "RoutingKeyName": "dead-letter-email-add",
                    "InitClassName": "Template.Logic.RabbitMqConsumers.DeadLetterEmailConsumer"
                }
            }
        ]
    }
        
```


```csharp
        public class EmailService
        {
            public void Enqueue<TModel>(TModel message)
            {
                if (this.configuration.GetValue<bool>("RabbitMq:IsRabbitMqEnabled") == true)
                {
                    QueueConfig? queueConfig = this.configuration.GetSection("RabbitMq:Queues").Get<List<QueueConfig>>().FirstOrDefault(q => q.Name == "Email");

                    this.rabbitMqService.Enqueue(queueConfig, message);
                }
            }
        }
        
```

```csharp
        public class UserService
        {
            public void SendVerificationEmailRabbitMq(UserEntity userEntity)
                {
                    string url = $"{configuration["BaseUrl"]}/api/Auth/verifyEmail";
                    string message = $"{url}?verifyEmailToken={userEntity.VerifyEmailToken}";

                    EmailModel emailQueueMessage = new()
                    {
                        To = userEntity.Email,
                        Subject = "Verify email",
                        Message = message,
                        IsMessageHtml = true
                    };

                    this.emailService.Enqueue(emailQueueMessage);
                }
        }
        
```

```csharp
    public class RabbitMqQueueService : IRabbitMqQueueService
    {
        private readonly IConnection? serverConnection;
        private readonly ILocalizationService localizationService;
        private readonly IRabbitMqConfigService rabbitMqConfig;

        public RabbitMqQueueService(
            IConfiguration configuration,
            ILocalizationService localizationService,
            IRabbitMqConfigService rabbitMqConfig)
        {
            this.localizationService = localizationService;
            this.rabbitMqConfig = rabbitMqConfig;

            if (configuration.GetValue<bool>("RabbitMq:IsRabbitMqEnabled") == true)
            {
                this.serverConnection = this.rabbitMqConfig.GetServerConnection();
            }
        }

        public void Enqueue<TModel>(QueueConfig? queueConfig, TModel message)
        {
            if (queueConfig == null)
            {
                throw new EmptyQueueConfigException(this.localizationService);
            }

            if (serverConnection == null)
            {
                throw new RabbitMqInvalidServerConnectionException(this.localizationService);
            }

            this.rabbitMqConfig.ConfigureQueueIfNotExist(queueConfig);

            byte[] serializedMessage = SerializeMessageForQueue(message);

            using var channel = serverConnection.CreateModel();

            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2;

            channel.BasicPublish(
                queueConfig.MainQueue.ExchangeName,
                queueConfig.MainQueue.RoutingKeyName,
                properties,
                serializedMessage);
        }

        private static byte[] SerializeMessageForQueue<TModel>(TModel message)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            return Encoding.UTF8.GetBytes(json);
        }
    }

```

```csharp
    public class RabbitMqConfigService : IRabbitMqConfigService
    {
        private readonly IConnection? serverConnection;
        private readonly ILocalizationService localizationService;
        private readonly IConfiguration configuration;

        public RabbitMqConfigService(
            IConfiguration configuration,
            ILocalizationService localizationService)
        {
            this.configuration = configuration;
            this.localizationService = localizationService;

            if (configuration.GetValue<bool>("RabbitMq:IsRabbitMqEnabled") == true)
            {
                this.serverConnection = this.ConnectToRabbitMqServer();
            }
        }

        public IConnection GetServerConnection()
        {
            if (serverConnection == null)
            {
                throw new RabbitMqInvalidServerConnectionException(this.localizationService);
            }

            return this.serverConnection;
        }

        public void ConfigureQueueIfNotExist(QueueConfig queueConfig)
        {
            if (serverConnection == null)
            {
                throw new RabbitMqInvalidServerConnectionException(this.localizationService);
            }

            Queue mainQueue = queueConfig.MainQueue;
            Queue deadLetterQueue = queueConfig.DeadLetterQueue;

            using var channel = serverConnection.CreateModel();

            channel.ExchangeDeclare(deadLetterQueue.ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false);
            channel.QueueDeclare(deadLetterQueue.QueueName, durable: true, exclusive: false, autoDelete: false, null);
            channel.QueueBind(deadLetterQueue.QueueName, deadLetterQueue.ExchangeName, deadLetterQueue.RoutingKeyName);

            channel.ExchangeDeclare(mainQueue.ExchangeName, ExchangeType.Direct, durable: true, autoDelete: false);

            channel.QueueDeclare(
                mainQueue.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", deadLetterQueue.ExchangeName },
                    { "x-dead-letter-routing-key", deadLetterQueue.RoutingKeyName}
                });

            channel.QueueBind(mainQueue.QueueName, mainQueue.ExchangeName, mainQueue.RoutingKeyName, null);
        }

        private IConnection ConnectToRabbitMqServer()
        {
            var connectionFactory = new ConnectionFactory
            {
                Uri = new Uri(configuration["ConnectionStrings:RabbitMqConnection"]),
                AutomaticRecoveryEnabled = true,
                DispatchConsumersAsync = true
            };

            return connectionFactory.CreateConnection();
        }
    }
```

```csharp
        public abstract class BaseConsumer<TModel>
    {
        protected IConfiguration Configuration { get; init; }
        protected ILocalizationService LocalizationService { get; init; }
        protected RabbitMqService RabbitMQService { get; init; }

        private readonly string consumerQueueName;

        private readonly QueueConfig queueConfig;

        private readonly IModel channel;

        public BaseConsumer(IServiceProvider serviceProvider, QueueConfig queueConfig, Queue queue)
        {
            this.Configuration = serviceProvider.GetRequiredService<IConfiguration>();
            this.RabbitMQService = serviceProvider.GetRequiredService<RabbitMqService>();
            this.LocalizationService = serviceProvider.GetRequiredService<ILocalizationService>();

            this.queueConfig = queueConfig;

            this.RabbitMQService.ConfigureQueueIfNotExist(queueConfig);

            this.channel = this.RabbitMQService.GetServerConnection().CreateModel();

            this.consumerQueueName = queue.QueueName;

            Subscribe();
        }

        private void Subscribe()
        {
            if (queueConfig.UseSingleMessageRecieve)
            {
                this.channel.BasicQos(0, 1, false);
            }

            AsyncEventingBasicConsumer eventingBasicConsumer = new(this.channel);

            eventingBasicConsumer.Received += this.ReceivedMessage;

            this.channel.BasicConsume(consumerQueueName, false, eventingBasicConsumer);
        }

        protected abstract Task OnReceivedMessage(TModel model);

        protected void DisacknowledgeMessage(BasicDeliverEventArgs e)
        {
            this.channel.BasicNack(e.DeliveryTag, false, false);
        }

        protected void AcknowledgeMessage(BasicDeliverEventArgs e)
        {
            this.channel.BasicAck(e.DeliveryTag, false);
        }

        private async Task ReceivedMessage(object sender, BasicDeliverEventArgs e)
        {
            TModel? model = DeserializeMessageFromQueue<TModel>(e.Body.ToArray());

            if (model == null)
            {
                throw new InvalidDeserializedMessageException(this.LocalizationService);
            }

            try
            {
                await this.OnReceivedMessage(model);
                AcknowledgeMessage(e);
            }
            catch (Exception)
            {
                DisacknowledgeMessage(e);
            }
        }

        private static T? DeserializeMessageFromQueue<T>(byte[] bytes)
        {
            string jsonString = Encoding.UTF8.GetString(bytes);

            return JsonConvert.DeserializeObject<T>(jsonString);
        }
    }
        
```

```csharp
    public class EmailConsumer : BaseConsumer<EmailModel>
    {
        private readonly EmailService emailService;

        public EmailConsumer(IServiceProvider serviceProvider, QueueConfig queueConfig, Queue queue)
            : base(serviceProvider, queueConfig, queue)
        {
            using var scope = serviceProvider.CreateScope();

            EmailService? emailService = scope.ServiceProvider.GetService<EmailService>();

            if (emailService == null)
            {
                throw new InvalidOperationException();
            }

            this.emailService = emailService;
        }

        protected override async Task OnReceivedMessage(EmailModel model)
        {
            await this.emailService.SendEmail(model);
        }
    }
        
```
