using System;

namespace MeuApp.Shared.Models;

public class Conta
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int DonoUsuarioId { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.Now;
}
