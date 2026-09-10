using System;

namespace MeuApp.Shared.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string SenhaSalt { get; set; } = string.Empty;
    
    public bool EmailConfirmado { get; set; } = false;
    public string? TokenConfirmacao { get; set; }
    public string? CodigoConfirmacao { get; set; }
    public DateTime? TokenExpiracao { get; set; }
    
    public DateTime DataCriacao { get; set; } = DateTime.Now;
    public DateTime? UltimoAcesso { get; set; }
    public bool Ativo { get; set; } = true;
}
