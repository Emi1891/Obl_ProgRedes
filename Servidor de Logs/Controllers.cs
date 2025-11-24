using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Servidor_de_Logs_
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetLogs(
            [FromQuery(Name = "user")] string? usuario,
            [FromQuery(Name = "class")] string? clase,
            [FromQuery(Name = "dateFrom")] string? fechaDesde,
            [FromQuery(Name = "dateTo")] string? fechaHasta,
            [FromQuery(Name = "event")] string? evento)
        {
            try
            {
                var filter = new LogFilter
                {
                    Usuario = usuario,
                    Clase = clase,
                    FechaDesde = string.IsNullOrEmpty(fechaDesde) ? null : DateTime.TryParse(fechaDesde, out var fd) ? fd : null,
                    FechaHasta = string.IsNullOrEmpty(fechaHasta) ? null : DateTime.TryParse(fechaHasta, out var fh) ? fh : null,
                    Evento = evento
                };

                var filteredLogs = LogService.GetFilteredLogs(filter);
                var logsFormatted = filteredLogs.Select(log => new
                {
                    user = log.Usuario,
                    @class = log.Clase,
                    @event = log.Evento,
                    date = log.Fecha.ToString("dd/MM/yyyy - HH:mm"),
                    details = log.Detalles
                }).ToList();
                return Ok(logsFormatted);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            try
            {
                var logs = LogService.GetAllLogs();
                var stats = new
                {
                    totalLogs = logs.Count,
                    logsByUser = logs.GroupBy(l => l.Usuario).ToDictionary(g => g.Key, g => g.Count()),
                    logsByClass = logs.GroupBy(l => l.Clase).ToDictionary(g => g.Key, g => g.Count()),
                    logsByEvent = logs.GroupBy(l => l.Evento).ToDictionary(g => g.Key, g => g.Count()),
                    oldestDate = logs.Any() ? logs.Min(l => l.Fecha).ToString("dd/MM/yyyy - HH:mm") : (string?)null,
                    newestDate = logs.Any() ? logs.Max(l => l.Fecha).ToString("dd/MM/yyyy - HH:mm") : (string?)null
                };
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("filters")]
        public IActionResult GetFiltersInfo()
        {
            var filterInfo = new
            {
                description = "Available filters for the /logs endpoint",
                filters = new[]
                {
                    new { name = "user", type = "string", description = "Filter by username (partial search, case-insensitive)" },
                    new { name = "class", type = "string", description = "Filter by class name (partial search, case-insensitive)" },
                    new { name = "dateFrom", type = "datetime", description = "Filter logs from this date (format: YYYY-MM-DD or YYYY-MM-DDTHH:mm:ss)" },
                    new { name = "dateTo", type = "datetime", description = "Filter logs until this date (format: YYYY-MM-DD or YYYY-MM-DDTHH:mm:ss)" },
                    new { name = "event", type = "string", description = "Filter by event type (partial search, case-insensitive)" }
                },
                example = "/logs?user=admin&class=Yoga&dateFrom=2024-01-01&event=enrollment",
                note = "All filters are optional and can be combined. Results are ordered by date descending."
            };
            return Ok(filterInfo);
        }
    }

    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok("OK");
        }
    }
}

