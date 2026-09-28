using System;

namespace MeuApp.Shared.Models;

public class MembroTime
{
    public int Id { get; set; }
    public int ContaId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Papel { get; set; } = "Colaborador"; // "Administrador" ou "Colaborador"
    public DateTime DataAdicao { get; set; } = DateTime.Now;
    public int? AdicionadoPorUsuarioId { get; set; }
    public bool Ativo { get; set; } = true;
}
