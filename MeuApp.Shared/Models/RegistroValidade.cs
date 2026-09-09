namespace MeuApp.Shared.Models;

public class RegistroValidade
{
    public int Id { get; set; }

    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }

    public int LojaId { get; set; }
    public Loja? Loja { get; set; }

    public DateTime DataValidade { get; set; }
    public DateTime DataColeta { get; set; } = DateTime.Today;
    public bool EmPromocao { get; set; } = false;
    public string Status { get; set; } = "Ativo"; // "Ativo" ou "Baixado"

    public DateTime? DataBaixa { get; set; }
    public string? MotivoBaixa { get; set; }

    // Propriedades calculadas auxiliares para UI
    public int DiasRestantes => (DataValidade.Date - DateTime.Today).Days;
    public bool EstaVencido => DiasRestantes < 0;

    public string MensagemPrazo => DiasRestantes switch
    {
        < 0 => $"ALERTA: Verificar na Loja! Vencido há {Math.Abs(DiasRestantes)} {(Math.Abs(DiasRestantes) == 1 ? "dia" : "dias")}",
        0 => "Vence hoje!",
        1 => "Vence amanhã (1 dia restante)",
        _ => $"Vence em {DiasRestantes} dias"
    };
}
