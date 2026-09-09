namespace MeuApp.Shared.Models;

public class Loja
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string CodigoLoja { get; set; } = string.Empty;

    public List<RegistroValidade> Validades { get; set; } = new();
}
