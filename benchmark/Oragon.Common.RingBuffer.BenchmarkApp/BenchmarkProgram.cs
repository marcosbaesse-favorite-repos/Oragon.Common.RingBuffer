﻿using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Oragon.Common.RingBuffer.Specialized;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oragon.Common.RingBuffer.BenchmarkApp
{

    [Config(typeof(Config))]
    [SimpleJob(RunStrategy.ColdStart, RuntimeMoniker.NetCoreApp50, targetCount: 5)]
    //[MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class BenchmarkProgram
    {
        DisposableRingBuffer<IConnection> connectionRingBuffer;
        DisposableRingBuffer<IModel> modelRingBuffer;
        ConnectionFactory connectionFactory;
        ReadOnlyMemory<byte> message;

        private class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.Dry);
                AddLogger(ConsoleLogger.Default);
                AddColumn(TargetMethodColumn.Method);
                AddColumn(StatisticColumn.AllStatistics);
                AddExporter(RPlotExporter.Default, CsvExporter.Default);
                AddAnalyser(EnvironmentAnalyser.Default);
                UnionRule = ConfigUnionRule.AlwaysUseLocal;
            }
        }

        public BenchmarkProgram()
        {
        }

        [GlobalSetup]
        public void GlobalSetup()
        {
            message = new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes("0"));

            connectionFactory = new ConnectionFactory()
            {
                Port = 5672,
                HostName = "localhost",
                UserName = "logUser",
                Password = "logPwd",
                VirtualHost = "EnterpriseLog",
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromMinutes(1)
            };

            Func<IConnection> getConnection = () => connectionFactory.CreateConnection();
            Func<IModel> getModel = () =>
            {
                using var connection = connectionRingBuffer.Accquire();
                return connection.Current.CreateModel();
            };

            connectionRingBuffer = new DisposableRingBuffer<IConnection>(3, getConnection, TimeSpan.FromMilliseconds(10));
            modelRingBuffer = new DisposableRingBuffer<IModel>(10, getModel, TimeSpan.FromMilliseconds(10));

        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            modelRingBuffer.Dispose();
            connectionRingBuffer.Dispose();
        }

        public void CreateQueue(IModel model, string queueName) =>
            model.QueueDeclare(queueName, true, false, false, null);

        public uint GetMessageCount(IModel model, string queueName) =>
            model.MessageCount(queueName);

        private static void Send(IModel channel, string queueName, ReadOnlyMemory<byte> message)
        {
            try
            {
                var prop = channel.CreateBasicProperties();
                prop.DeliveryMode = 1;
                channel.BasicPublish(string.Empty, queueName, prop, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

        [Benchmark]
        public int WithRingBuffer()
        {
            string queueName = $"WithRingBuffer-{Guid.NewGuid().ToString("D")}";
            using (var accquisiton = modelRingBuffer.Accquire())
            {
                CreateQueue(accquisiton.Current, queueName);
            }

            for (var i = 0; i < 5; i++)
                for (var j = 0; j < 1000; j++)
                    using (var accquisiton = modelRingBuffer.Accquire())
                    {
                        Send(accquisiton.Current, queueName, message);
                    }
            return 0;
        }

        [Benchmark]
        public int WithoutRingBuffer()
        {
            string queueName = $"WithoutRingBuffer-{Guid.NewGuid().ToString("D")}";
            using (var connection = connectionFactory.CreateConnection())
            {
                using (var model = connection.CreateModel())
                {
                    CreateQueue(model, queueName);
                }
            }

            for (var i = 0; i < 5; i++)
                using (var connection = connectionFactory.CreateConnection())
                {
                    for (var j = 0; j < 1000; j++)
                        using (var model = connection.CreateModel())
                        {
                            Send(model, queueName, message);
                        }
                }
            return 0;
        }

        internal static void Analyse(Summary summary)
        {
            string nameColumn = TargetMethodColumn.Method.ColumnName;
            string statisticColumn = StatisticColumn.Mean.ColumnName;

            //summary.Table.FullContentWithHeader.



        }
    }
}
