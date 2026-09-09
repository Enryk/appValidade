namespace MeuApp.Shared.Models;

public class Produto
{
    public int Id { get; set; }
    public string CodigoBarras { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    public List<RegistroValidade> Validades { get; set; } = new();
}
