using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitoramentoInfraProcess.Models;

namespace MonitoramentoInfraProcess
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly JsonSerializerOptions _jsonOptions;

        public Worker(ILogger<Worker> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string urlBaseHealthChecks =
                _configuration.GetSection("UrlBaseHealthChecks").Value;
            string urlLogicAppSlack =
                _configuration.GetSection("UrlLogicAppSlack").Value;

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Iniciando processo de monitoramento em: {time}", DateTimeOffset.Now);

                using (var client = new HttpClient())
                {
                    _logger.LogInformation("Obtendo status dos Health Checks em: {time}", DateTimeOffset.Now);

                    client.BaseAddress = new Uri(urlBaseHealthChecks);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));

                    // Envio da requisição a fim de obter o status
                    // dos Health Checks
                    HttpResponseMessage respHealthChecks = client.GetAsync(
                        "status-monitoramento").Result;

                    if (respHealthChecks.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        string conteudo =
                            respHealthChecks.Content.ReadAsStringAsync().Result;
                        Console.WriteLine(conteudo);

                        var statusHealthChecks = JsonSerializer
                            .Deserialize<StatusHealthCheck[]>(conteudo, _jsonOptions)
                            .Where(s => s.Status == "Unhealthy");

                        using (var clientLogicAppSlack = new HttpClient())
                        {
                            clientLogicAppSlack.BaseAddress = new Uri(urlLogicAppSlack);
                            clientLogicAppSlack.DefaultRequestHeaders.Accept.Clear();
                            clientLogicAppSlack.DefaultRequestHeaders.Accept.Add(
                                new MediaTypeWithQualityHeaderValue("application/json"));

                            foreach (var status in statusHealthChecks)
                            {
                                var requestMessage =
                                    new HttpRequestMessage(HttpMethod.Post, String.Empty);
                                requestMessage.Content = new StringContent(
                                    JsonSerializer.Serialize(new
                                    {
                                        dependency = status.HealthCheck,
                                        message = status.Error,
                                    }), Encoding.UTF8, "application/json");

                                var respLogicApp = clientLogicAppSlack
                                    .SendAsync(requestMessage).Result;
                                respLogicApp.EnsureSuccessStatusCode();

                                _logger.LogError(
                                    $"Status do Health Check {status.HealthCheck} enviado para o Slack");
                            }
                        }
                    }
                }

                _logger.LogInformation("Concluindo verificacao de Health Checks em: {time}", DateTimeOffset.Now);
                await Task.Delay(60000, stoppingToken);
            }
        }
    }
}