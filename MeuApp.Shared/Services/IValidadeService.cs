using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public interface IValidadeService
{
    Task InicializarBancoESeedAsync();

    // Lojas
    Task<List<Loja>> GetLojasAsync();
    Task<Loja?> GetLojaPorIdAsync(int id);
    Task<Loja> CriarLojaAsync(Loja loja);
    Task<Loja?> AtualizarLojaAsync(int id, string nome, string codigoLoja);
    Task ExcluirLojaAsync(int id);

    // Coleta e Produtos (Catálogo Compartilhado)
    Task<Produto?> BuscarProdutoPorCodigoBarrasAsync(string codigoBarras);
    Task<List<RegistroValidade>> GetValidadesAtivasDoProdutoNaLojaAsync(int produtoId, int lojaId);
    Task<Produto> CadastrarProdutoComValidadeAsync(int lojaId, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao);
    Task<Produto> CadastrarProdutoComValidadeMultiplasLojasAsync(IEnumerable<int> lojasIds, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao);
    Task<RegistroValidade> AdicionarValidadeAoProdutoAsync(int produtoId, int lojaId, DateTime dataValidade, bool emPromocao);
    Task<List<RegistroValidade>> AdicionarValidadeMultiplasLojasAsync(int produtoId, IEnumerable<int> lojasIds, DateTime dataValidade, bool emPromocao);
    Task AtualizarNomeProdutoAsync(int produtoId, string novoNome);

    // Cópia e Transferência entre Lojas
    Task<RegistroValidade?> CopiarValidadeAsync(int validadeId, int lojaDestinoId);
    Task<int> CopiarValidadeParaTodasLojasAsync(int validadeId);
    Task TransferirValidadeAsync(int validadeId, int lojaDestinoId);

    // Monitoramento
    Task<List<RegistroValidade>> GetValidadesAtivasAsync(int? lojaId, string filtroRapido, DateTime? dataInicio = null, DateTime? dataFim = null);
    Task AlternarPromocaoAsync(int validadeId, bool emPromocao);
    Task DarBaixaAsync(int validadeId, string motivo);

    // Histórico de Baixas
    Task DesfazerBaixaAsync(int validadeId);
    Task<List<RegistroValidade>> GetHistoricoBaixasAsync(int? lojaId, DateTime? dataInicio = null, DateTime? dataFim = null, string? motivo = null);
    string GerarCsvHistorico(IEnumerable<RegistroValidade> registros);
}
