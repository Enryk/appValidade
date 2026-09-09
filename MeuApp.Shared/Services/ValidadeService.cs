using System.Text;
using Microsoft.EntityFrameworkCore;
using MeuApp.Shared.Data;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public class ValidadeService : IValidadeService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ValidadeService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InicializarBancoESeedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Migração defensiva para bancos SQLite existentes
        try
        {
            var conn = context.Database.GetDbConnection();
            await conn.OpenAsync();

            using var cmdCheck = conn.CreateCommand();
            cmdCheck.CommandText = "PRAGMA table_info(RegistrosValidade);";
            using var reader = await cmdCheck.ExecuteReaderAsync();
            var temLojaId = false;
            var temTabela = false;
            while (await reader.ReadAsync())
            {
                temTabela = true;
                var col = reader.GetString(1);
                if (col.Equals("LojaId", StringComparison.OrdinalIgnoreCase))
                {
                    temLojaId = true;
                    break;
                }
            }
            await reader.CloseAsync();

            if (temTabela && !temLojaId)
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = @"
                    ALTER TABLE RegistrosValidade ADD COLUMN LojaId INTEGER NOT NULL DEFAULT 1;
                    UPDATE RegistrosValidade SET LojaId = (SELECT LojaId FROM Produtos WHERE Produtos.Id = RegistrosValidade.ProdutoId) WHERE EXISTS (SELECT 1 FROM Produtos WHERE Produtos.Id = RegistrosValidade.ProdutoId);
                ";
                await cmdAlter.ExecuteNonQueryAsync();
            }
        }
        catch
        {
        }

        await context.Database.EnsureCreatedAsync();

        // Deduplica produtos caso existam produtos com mesmo CodigoBarras para lojas diferentes
        try
        {
            var produtos = await context.Produtos.ToListAsync();
            var grupos = produtos.GroupBy(p => p.CodigoBarras).Where(g => g.Count() > 1).ToList();
            foreach (var g in grupos)
            {
                var principal = g.First();
                var repetidos = g.Skip(1).ToList();
                foreach (var rep in repetidos)
                {
                    var validades = await context.RegistrosValidade.Where(r => r.ProdutoId == rep.Id).ToListAsync();
                    foreach (var v in validades)
                    {
                        v.ProdutoId = principal.Id;
                    }
                    context.Produtos.Remove(rep);
                }
            }
            await context.SaveChangesAsync();
        }
        catch
        {
        }

        // Carga inicial (Seed) caso o banco esteja vazio
        if (!await context.Lojas.AnyAsync())
        {
            var loja1 = new Loja { Nome = "Loja 01 - Centro", CodigoLoja = "LJ-01" };
            var loja2 = new Loja { Nome = "Loja 02 - Zona Sul", CodigoLoja = "LJ-02" };
            context.Lojas.AddRange(loja1, loja2);
            await context.SaveChangesAsync();

            var hoje = DateTime.Today;

            var p1 = new Produto { CodigoBarras = "7891000100101", Nome = "Iogurte Natural 170g" };
            var p2 = new Produto { CodigoBarras = "7891000200202", Nome = "Queijo Mussarela Fatiado 500g" };
            var p3 = new Produto { CodigoBarras = "7891000300303", Nome = "Pão de Forma Tradicional 400g" };
            var p4 = new Produto { CodigoBarras = "7891000400404", Nome = "Presunto Cozido 200g" };
            var p5 = new Produto { CodigoBarras = "7891000500505", Nome = "Leite Longa Vida 1L" };

            context.Produtos.AddRange(p1, p2, p3, p4, p5);
            await context.SaveChangesAsync();

            var validades = new List<RegistroValidade>
            {
                new() { ProdutoId = p4.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(-3), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p1.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(4), EmPromocao = true, Status = "Ativo" },
                new() { ProdutoId = p2.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(9), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p3.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(14), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p5.Id, LojaId = loja2.Id, DataColeta = hoje, DataValidade = hoje.AddDays(45), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p2.Id, LojaId = loja1.Id, DataColeta = hoje.AddDays(-7), DataValidade = hoje.AddDays(-5), EmPromocao = true, Status = "Baixado", DataBaixa = DateTime.Now.AddDays(-1), MotivoBaixa = "Descarte/Vencido" }
            };

            context.RegistrosValidade.AddRange(validades);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<Loja>> GetLojasAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas.OrderBy(l => l.Nome).ToListAsync();
    }

    public async Task<Loja?> GetLojaPorIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas.FindAsync(id);
    }

    public async Task<Loja> CriarLojaAsync(Loja loja)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Lojas.Add(loja);
        await context.SaveChangesAsync();
        return loja;
    }

    public async Task<Loja?> AtualizarLojaAsync(int id, string nome, string codigoLoja)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FindAsync(id);
        if (loja != null)
        {
            loja.Nome = nome.Trim();
            loja.CodigoLoja = codigoLoja.Trim().ToUpperInvariant();
            await context.SaveChangesAsync();
        }
        return loja;
    }

    public async Task ExcluirLojaAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FindAsync(id);
        if (loja != null)
        {
            context.Lojas.Remove(loja);
            await context.SaveChangesAsync();
        }
    }

    public async Task<Produto?> BuscarProdutoPorCodigoBarrasAsync(string codigoBarras)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        return await context.Produtos
            .Include(p => p.Validades.Where(v => v.Status == "Ativo"))
                .ThenInclude(v => v.Loja)
            .FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);
    }

    public async Task<List<RegistroValidade>> GetValidadesAtivasDoProdutoNaLojaAsync(int produtoId, int lojaId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RegistrosValidade
            .Include(r => r.Loja)
            .Include(r => r.Produto)
            .Where(r => r.ProdutoId == produtoId && r.LojaId == lojaId && r.Status == "Ativo")
            .OrderBy(r => r.DataValidade)
            .ToListAsync();
    }

    public async Task<Produto> CadastrarProdutoComValidadeAsync(int lojaId, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                CodigoBarras = codigoTratado,
                Nome = nome.Trim()
            };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(nome) && produto.Nome != nome.Trim())
        {
            produto.Nome = nome.Trim();
            await context.SaveChangesAsync();
        }

        var registro = new RegistroValidade
        {
            ProdutoId = produto.Id,
            LojaId = lojaId,
            DataColeta = DateTime.Today,
            DataValidade = dataValidade.Date,
            EmPromocao = emPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(registro);
        await context.SaveChangesAsync();

        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado))!;
    }

    public async Task<Produto> CadastrarProdutoComValidadeMultiplasLojasAsync(IEnumerable<int> lojasIds, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                CodigoBarras = codigoTratado,
                Nome = nome.Trim()
            };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(nome) && produto.Nome != nome.Trim())
        {
            produto.Nome = nome.Trim();
            await context.SaveChangesAsync();
        }

        foreach (var lojaId in lojasIds.Distinct())
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r => 
                r.ProdutoId == produto.Id && 
                r.LojaId == lojaId && 
                r.DataValidade == dataValidade.Date && 
                r.Status == "Ativo");

            if (!jaExiste)
            {
                context.RegistrosValidade.Add(new RegistroValidade
                {
                    ProdutoId = produto.Id,
                    LojaId = lojaId,
                    DataColeta = DateTime.Today,
                    DataValidade = dataValidade.Date,
                    EmPromocao = emPromocao,
                    Status = "Ativo"
                });
            }
        }

        await context.SaveChangesAsync();
        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado))!;
    }

    public async Task<RegistroValidade> AdicionarValidadeAoProdutoAsync(int produtoId, int lojaId, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var registro = new RegistroValidade
        {
            ProdutoId = produtoId,
            LojaId = lojaId,
            DataColeta = DateTime.Today,
            DataValidade = dataValidade.Date,
            EmPromocao = emPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(registro);
        await context.SaveChangesAsync();
        return registro;
    }

    public async Task<List<RegistroValidade>> AdicionarValidadeMultiplasLojasAsync(int produtoId, IEnumerable<int> lojasIds, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var novosRegistros = new List<RegistroValidade>();

        foreach (var lojaId in lojasIds.Distinct())
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
                r.ProdutoId == produtoId &&
                r.LojaId == lojaId &&
                r.DataValidade == dataValidade.Date &&
                r.Status == "Ativo");

            if (!jaExiste)
            {
                var reg = new RegistroValidade
                {
                    ProdutoId = produtoId,
                    LojaId = lojaId,
                    DataColeta = DateTime.Today,
                    DataValidade = dataValidade.Date,
                    EmPromocao = emPromocao,
                    Status = "Ativo"
                };
                context.RegistrosValidade.Add(reg);
                novosRegistros.Add(reg);
            }
        }

        await context.SaveChangesAsync();
        return novosRegistros;
    }

    public async Task<RegistroValidade?> CopiarValidadeAsync(int validadeId, int lojaDestinoId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var original = await context.RegistrosValidade.FindAsync(validadeId);
        if (original == null) return null;

        var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
            r.ProdutoId == original.ProdutoId &&
            r.LojaId == lojaDestinoId &&
            r.DataValidade == original.DataValidade.Date &&
            r.Status == "Ativo");

        if (jaExiste) return null;

        var copia = new RegistroValidade
        {
            ProdutoId = original.ProdutoId,
            LojaId = lojaDestinoId,
            DataColeta = DateTime.Today,
            DataValidade = original.DataValidade.Date,
            EmPromocao = original.EmPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(copia);
        await context.SaveChangesAsync();
        return copia;
    }

    public async Task<int> CopiarValidadeParaTodasLojasAsync(int validadeId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var original = await context.RegistrosValidade.FindAsync(validadeId);
        if (original == null) return 0;

        var outrasLojas = await context.Lojas
            .Where(l => l.Id != original.LojaId)
            .ToListAsync();

        int copiasCriadas = 0;
        foreach (var loja in outrasLojas)
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
                r.ProdutoId == original.ProdutoId &&
                r.LojaId == loja.Id &&
                r.DataValidade == original.DataValidade.Date &&
                r.Status == "Ativo");

            if (!jaExiste)
            {
                context.RegistrosValidade.Add(new RegistroValidade
                {
                    ProdutoId = original.ProdutoId,
                    LojaId = loja.Id,
                    DataColeta = DateTime.Today,
                    DataValidade = original.DataValidade.Date,
                    EmPromocao = original.EmPromocao,
                    Status = "Ativo"
                });
                copiasCriadas++;
            }
        }

        if (copiasCriadas > 0)
        {
            await context.SaveChangesAsync();
        }

        return copiasCriadas;
    }

    public async Task TransferirValidadeAsync(int validadeId, int lojaDestinoId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null && reg.LojaId != lojaDestinoId)
        {
            reg.LojaId = lojaDestinoId;
            await context.SaveChangesAsync();
        }
    }

    public async Task AtualizarNomeProdutoAsync(int produtoId, string novoNome)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var p = await context.Produtos.FindAsync(produtoId);
        if (p != null && !string.IsNullOrWhiteSpace(novoNome))
        {
            p.Nome = novoNome.Trim();
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<RegistroValidade>> GetValidadesAtivasAsync(int? lojaId, string filtroRapido, DateTime? dataInicio = null, DateTime? dataFim = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.Status == "Ativo");

        if (lojaId.HasValue && lojaId.Value > 0)
        {
            query = query.Where(r => r.LojaId == lojaId.Value);
        }

        var hoje = DateTime.Today;

        query = (filtroRapido ?? "").ToLowerInvariant() switch
        {
            "7dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(7)),
            "10dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(10)),
            "15dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(15)),
            "vencidos" => query.Where(r => r.DataValidade < hoje),
            "personalizado" when dataInicio.HasValue && dataFim.HasValue =>
                query.Where(r => r.DataValidade >= dataInicio.Value.Date && r.DataValidade <= dataFim.Value.Date),
            _ => query
        };

        return await query.OrderBy(r => r.DataValidade).ToListAsync();
    }

    public async Task AlternarPromocaoAsync(int validadeId, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.EmPromocao = emPromocao;
            await context.SaveChangesAsync();
        }
    }

    public async Task DarBaixaAsync(int validadeId, string motivo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.Status = "Baixado";
            reg.DataBaixa = DateTime.Now;
            reg.MotivoBaixa = string.IsNullOrWhiteSpace(motivo) ? "Descarte/Vencido" : motivo.Trim();
            await context.SaveChangesAsync();
        }
    }

    public async Task DesfazerBaixaAsync(int validadeId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.Status = "Ativo";
            reg.DataBaixa = null;
            reg.MotivoBaixa = null;
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<RegistroValidade>> GetHistoricoBaixasAsync(int? lojaId, DateTime? dataInicio = null, DateTime? dataFim = null, string? motivo = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.Status == "Baixado");

        if (lojaId.HasValue && lojaId.Value > 0)
        {
            query = query.Where(r => r.LojaId == lojaId.Value);
        }

        if (dataInicio.HasValue)
        {
            query = query.Where(r => r.DataBaixa != null && r.DataBaixa.Value.Date >= dataInicio.Value.Date);
        }

        if (dataFim.HasValue)
        {
            query = query.Where(r => r.DataBaixa != null && r.DataBaixa.Value.Date <= dataFim.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(motivo) && motivo != "Todos")
        {
            query = query.Where(r => r.MotivoBaixa == motivo);
        }

        return await query.OrderByDescending(r => r.DataBaixa).ToListAsync();
    }

    public string GerarCsvHistorico(IEnumerable<RegistroValidade> registros)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Loja;Código de Barras;Produto;Data Validade;Data Coleta;Data da Baixa;Motivo;Em Promoção");

        foreach (var item in registros)
        {
            var loja = SanitizarCampoCsv(item.Loja?.Nome);
            var codBarras = SanitizarCampoCsv(item.Produto?.CodigoBarras);
            var nome = SanitizarCampoCsv(item.Produto?.Nome);
            var validade = item.DataValidade.ToString("dd/MM/yyyy");
            var coleta = item.DataColeta.ToString("dd/MM/yyyy");
            var baixa = item.DataBaixa?.ToString("dd/MM/yyyy HH:mm:ss") ?? "N/D";
            var motivo = SanitizarCampoCsv(item.MotivoBaixa);
            var promo = item.EmPromocao ? "Sim" : "Não";

            sb.AppendLine($"{loja};{codBarras};{nome};{validade};{coleta};{baixa};{motivo};{promo}");
        }

        return sb.ToString();
    }

    private static string SanitizarCampoCsv(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "N/D";
        var tratado = valor.Replace(";", ",").Trim();
        // Previne CSV Formula Injection (DDE commands no Excel/Calc/Sheets)
        if (tratado.Length > 0 && (tratado[0] == '=' || tratado[0] == '+' || tratado[0] == '-' || tratado[0] == '@' || tratado[0] == '\t' || tratado[0] == '\r'))
        {
            tratado = "'" + tratado;
        }
        return tratado;
    }
}
