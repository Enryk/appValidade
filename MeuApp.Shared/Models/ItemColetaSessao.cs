namespace MeuApp.Shared.Models;

public class ItemColetaSessao
{
    public Guid IdTemp { get; set; } = Guid.NewGuid();
    public int? ProdutoId { get; set; }
    public string CodigoBarras { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public int LojaId { get; set; }
    public string LojaNome { get; set; } = string.Empty;
    public string LojaSigla { get; set; } = string.Empty;
    public DateTime DataValidade { get; set; }
    public bool EmPromocao { get; set; }
    public bool EhProdutoNovo { get; set; }
}
