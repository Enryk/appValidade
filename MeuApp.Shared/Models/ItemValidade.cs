namespace MeuApp.Shared.Models;

public enum StatusValidade
{
    Normal,
    Alerta,
    Vencido
}

public class ItemValidade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nome { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public DateTime DataValidade { get; set; }
    public int Quantidade { get; set; }
    public string Unidade { get; set; } = "un";

    public int DiasRestantes => (DataValidade.Date - DateTime.Today).Days;

    public StatusValidade Status => DiasRestantes switch
    {
        < 0 => StatusValidade.Vencido,
        <= 7 => StatusValidade.Alerta,
        _ => StatusValidade.Normal
    };
}
