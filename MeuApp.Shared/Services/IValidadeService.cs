using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public interface IValidadeService
{
    Task InicializarBancoESeedAsync();

    // Gestão de Times e Conta
    Task<Conta?> ObterContaAtualAsync(int? contaId = null);
    Task<int> ObterContaIdAtualAsync();
    Task<List<MembroTime>> GetMembrosTimeAsync(int? contaId = null);
    Task<MembroTime> AdicionarMembroTimeAsync(string email, string nome, string papel, int? contaId = null, string baseUrl = "");
    Task<bool> RemoverMembroTimeAsync(int membroId, int? contaId = null);
    Task<string> ObterPapelUsuarioAtualAsync();
    Task<bool> UsuarioAtualEhColaboradorAsync();

    // Lojas
    Task<List<Loja>> GetLojasAsync(int? contaId = null);
    Task<Loja?> GetLojaPorIdAsync(int id, int? contaId = null);
    Task<Loja> CriarLojaAsync(Loja loja, int? contaId = null);
    Task<Loja?> AtualizarLojaAsync(int id, string nome, string codigoLoja, int? contaId = null);
    Task ExcluirLojaAsync(int id, int? contaId = null);

    // Coleta e Produtos (Catálogo por Conta)
    Task<Produto?> BuscarProdutoPorCodigoBarrasAsync(string codigoBarras, int? contaId = null);
    Task<List<Produto>> BuscarProdutosPorTermoAsync(string termo, int limite = 10, int? contaId = null);
    Task<List<RegistroValidade>> GetValidadesAtivasDoProdutoNaLojaAsync(int produtoId, int lojaId, int? contaId = null);
    Task<Produto> CadastrarProdutoComValidadeAsync(int lojaId, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao, int? contaId = null);
    Task<Produto> CadastrarProdutoComValidadeMultiplasLojasAsync(IEnumerable<int> lojasIds, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao, int? contaId = null);
    Task<RegistroValidade> AdicionarValidadeAoProdutoAsync(int produtoId, int lojaId, DateTime dataValidade, bool emPromocao, int? contaId = null);
    Task<List<RegistroValidade>> AdicionarValidadeMultiplasLojasAsync(int produtoId, IEnumerable<int> lojasIds, DateTime dataValidade, bool emPromocao, int? contaId = null);
    Task AtualizarNomeProdutoAsync(int produtoId, string novoNome, int? contaId = null);
    Task SalvarLoteColetasAsync(IEnumerable<ItemColetaSessao> itens, int? contaId = null);

    // Monitoramento
    Task<List<RegistroValidade>> GetValidadesAtivasAsync(int? lojaId, string filtroRapido, DateTime? dataInicio = null, DateTime? dataFim = null, int? contaId = null);
    Task AlternarPromocaoAsync(int validadeId, bool emPromocao, int? contaId = null);
    Task DarBaixaAsync(int validadeId, string motivo, int? contaId = null);

    // Histórico de Baixas
    Task DesfazerBaixaAsync(int validadeId, int? contaId = null);
    Task<List<RegistroValidade>> GetHistoricoBaixasAsync(int? lojaId, DateTime? dataInicio = null, DateTime? dataFim = null, string? motivo = null, int? contaId = null);
    string GerarCsvHistorico(IEnumerable<RegistroValidade> registros);

    // Importação de Planilhas (Excel / CSV)
    Task<List<ItemImportacaoPlanilha>> ProcessarPreviaPlanilhaAsync(Stream stream, string nomeArquivo, int lojaDestinoId, int? contaId = null);
    Task<ResultadoImportacao> ExecutarImportacaoAsync(int lojaDestinoId, List<ItemImportacaoPlanilha> itens, int? contaId = null);
}
