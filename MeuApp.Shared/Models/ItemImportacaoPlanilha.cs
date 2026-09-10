namespace MeuApp.Shared.Models;

public class ItemImportacaoPlanilha
{
    public int Linha { get; set; }
    public string CodigoBarras { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public DateTime? DataValidade { get; set; }
    public string? LojaOriginal { get; set; }
    public string? StatusOriginal { get; set; }
    public bool Valido { get; set; }
    public string? MotivoInvalido { get; set; }
    public bool ProdutoExiste { get; set; }
    public bool ValidadeJaExisteNaLoja { get; set; }
}

public class ResultadoImportacao
{
    public int TotalLidos { get; set; }
    public int ProdutosCadastrados { get; set; }
    public int ProdutosAtualizados { get; set; }
    public int ValidadesInseridas { get; set; }
    public int ValidadesIgnoradasDuplicadas { get; set; }
    public int LinhasInvalidas { get; set; }
    public bool Sucesso { get; set; }
    public string Mensagem { get; set; } = string.Empty;
}
