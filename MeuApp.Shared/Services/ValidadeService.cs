using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using MeuApp.Shared.Data;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public class ValidadeService : IValidadeService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IAuthService _authService;
    private readonly IEmailService? _emailService;

    public ValidadeService(
        IDbContextFactory<AppDbContext> contextFactory, 
        IAuthService authService,
        IEmailService? emailService = null)
    {
        _contextFactory = contextFactory;
        _authService = authService;
        _emailService = emailService;
    }

    public async Task<int> ObterContaIdAtualAsync()
    {
        var usuario = await _authService.ObterUsuarioLogadoAsync();
        if (usuario != null && usuario.ContaId > 0)
            return usuario.ContaId;

        throw new UnauthorizedAccessException("Sessão expirada ou usuário não autenticado.");
    }

    private async Task<int> ResolverContaIdAsync(int? contaId)
    {
        var contaAutenticada = await ObterContaIdAtualAsync();
        if (contaId.HasValue && contaId.Value > 0)
        {
            if (contaId.Value != contaAutenticada)
            {
                throw new UnauthorizedAccessException("Tentativa de acesso não autorizada a dados de outra organização.");
            }
            return contaId.Value;
        }
        return contaAutenticada;
    }

    public async Task InicializarBancoESeedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Migração defensiva apenas para bancos SQLite locais existentes
        if (context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await context.Database.EnsureCreatedAsync();
            try
            {
                var conn = context.Database.GetDbConnection();
                await conn.OpenAsync();

            // 1. Criação defensiva das tabelas Contas e MembrosTime
            using var cmdCreateContas = conn.CreateCommand();
            cmdCreateContas.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Contas"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""Nome"" TEXT NOT NULL,
                    ""DonoUsuarioId"" INTEGER NOT NULL,
                    ""DataCriacao"" TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ""MembrosTime"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""ContaId"" INTEGER NOT NULL,
                    ""Email"" TEXT NOT NULL,
                    ""Nome"" TEXT NOT NULL,
                    ""Papel"" TEXT NOT NULL,
                    ""DataAdicao"" TEXT NOT NULL,
                    ""AdicionadoPorUsuarioId"" INTEGER NULL,
                    ""Ativo"" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_MembrosTime_ContaId"" ON ""MembrosTime"" (""ContaId"");
                CREATE INDEX IF NOT EXISTS ""IX_MembrosTime_Email"" ON ""MembrosTime"" (""Email"");
            ";
            await cmdCreateContas.ExecuteNonQueryAsync();

            // 2. Garante coluna ContaId em todas as tabelas principais
            async Task GarantirColunaContaIdAsync(string tabela)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({tabela});";
                using var r = await cmd.ExecuteReaderAsync();
                var temContaId = false;
                var temTab = false;
                while (await r.ReadAsync())
                {
                    temTab = true;
                    var col = r.GetString(1);
                    if (col.Equals("ContaId", StringComparison.OrdinalIgnoreCase))
                    {
                        temContaId = true;
                        break;
                    }
                }
                await r.CloseAsync();

                if (temTab && !temContaId)
                {
                    using var cmdAlt = conn.CreateCommand();
                    cmdAlt.CommandText = $"ALTER TABLE {tabela} ADD COLUMN ContaId INTEGER NOT NULL DEFAULT 1;";
                    await cmdAlt.ExecuteNonQueryAsync();
                }
            }

            await GarantirColunaContaIdAsync("Usuarios");
            await GarantirColunaContaIdAsync("Lojas");
            await GarantirColunaContaIdAsync("Produtos");
            await GarantirColunaContaIdAsync("RegistrosValidade");

            // Garante coluna DeveAlterarSenha na tabela Usuarios
            async Task GarantirColunaDeveAlterarSenhaAsync()
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(Usuarios);";
                using var r = await cmd.ExecuteReaderAsync();
                var temCol = false;
                while (await r.ReadAsync())
                {
                    if (r.GetString(1).Equals("DeveAlterarSenha", StringComparison.OrdinalIgnoreCase))
                    {
                        temCol = true;
                        break;
                    }
                }
                await r.CloseAsync();

                if (!temCol)
                {
                    using var cmdAlt = conn.CreateCommand();
                    cmdAlt.CommandText = "ALTER TABLE Usuarios ADD COLUMN DeveAlterarSenha INTEGER NOT NULL DEFAULT 0;";
                    await cmdAlt.ExecuteNonQueryAsync();
                }
            }
            await GarantirColunaDeveAlterarSenhaAsync();

            // 3. Atualiza índice único de produtos para ser por (ContaId, CodigoBarras)
            using var cmdCheckProdTable = conn.CreateCommand();
            cmdCheckProdTable.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Produtos';";
            var prodExiste = await cmdCheckProdTable.ExecuteScalarAsync();

            if (prodExiste != null)
            {
                using var cmdIndexProd = conn.CreateCommand();
                cmdIndexProd.CommandText = @"
                    DROP INDEX IF EXISTS ""IX_Produtos_CodigoBarras"";
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Produtos_ContaId_CodigoBarras"" ON ""Produtos"" (""ContaId"", ""CodigoBarras"");
                ";
                await cmdIndexProd.ExecuteNonQueryAsync();
            }

            // 4. Garante que dados legados anteriores à migração pertençam à Conta 1
            using var cmdContaPadrao = conn.CreateCommand();
            cmdContaPadrao.CommandText = @"
                INSERT INTO Contas (Id, Nome, DonoUsuarioId, DataCriacao)
                SELECT 1, 'Conta Principal', 1, strftime('%Y-%m-%d %H:%M:%S', 'now')
                WHERE NOT EXISTS (SELECT 1 FROM Contas WHERE Id = 1);

                UPDATE Lojas SET ContaId = 1 WHERE ContaId = 0;
                UPDATE Produtos SET ContaId = 1 WHERE ContaId = 0;
                UPDATE RegistrosValidade SET ContaId = 1 WHERE ContaId = 0;
                UPDATE Usuarios SET ContaId = 1 WHERE ContaId = 0;
            ";
            await cmdContaPadrao.ExecuteNonQueryAsync();
        }
        catch
        {
        }
    }
    else
    {
        // PostgreSQL (Supabase) ou outro banco relacional
        try
        {
            var conn = context.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count == 0)
            {
                var script = context.Database.GenerateCreateScript();
                using var cmdDdl = conn.CreateCommand();
                cmdDdl.CommandText = script;
                await cmdDdl.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            await context.Database.EnsureCreatedAsync();
        }
    }

    // ATENÇÃO: NÃO SEEDAR LOJAS OU PRODUTOS AUTOMATICAMENTE!
    // Novos usuários cadastrados na tela de início devem iniciar 100% do zero.
}

    // ==========================================
    // GESTÃO DE TIMES E CONTA
    // ==========================================

    public async Task<Conta?> ObterContaAtualAsync(int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Contas.FirstOrDefaultAsync(c => c.Id == cid);
    }

    public async Task<List<MembroTime>> GetMembrosTimeAsync(int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.MembrosTime
            .AsNoTracking()
            .Where(m => m.ContaId == cid && m.Ativo)
            .OrderBy(m => m.DataAdicao)
            .ToListAsync();
    }

    public async Task<MembroTime> AdicionarMembroTimeAsync(string email, string nome, string papel, int? contaId = null, string baseUrl = "")
    {
        var cid = await ResolverContaIdAsync(contaId);
        email = email.Trim().ToLowerInvariant();
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existente = await context.MembrosTime.FirstOrDefaultAsync(m => m.ContaId == cid && m.Email == email);
        if (existente != null)
        {
            existente.Ativo = true;
            existente.Nome = string.IsNullOrWhiteSpace(nome) ? existente.Nome : nome.Trim();
            existente.Papel = string.IsNullOrWhiteSpace(papel) ? "Colaborador" : papel.Trim();
            await context.SaveChangesAsync();
            return existente;
        }

        var usuarioLogado = await _authService.ObterUsuarioLogadoAsync();
        var novo = new MembroTime
        {
            ContaId = cid,
            Email = email,
            Nome = string.IsNullOrWhiteSpace(nome) ? email : nome.Trim(),
            Papel = string.IsNullOrWhiteSpace(papel) ? "Colaborador" : papel.Trim(),
            DataAdicao = DateTime.Now,
            AdicionadoPorUsuarioId = usuarioLogado?.Id,
            Ativo = true
        };
        context.MembrosTime.Add(novo);

        // Se o usuário do e-mail já estiver cadastrado no sistema, associa ele à conta do time.
        // Se ainda não existir, cria o usuário com token de ativação para definir a própria senha
        var usuarioExistente = await context.Usuarios.FirstOrDefaultAsync(u => u.Email == email);
        if (usuarioExistente != null)
        {
            usuarioExistente.ContaId = cid;
        }
        else
        {
            var token = Guid.NewGuid().ToString("N");
            var codigo = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var nomeFinal = string.IsNullOrWhiteSpace(nome) ? email.Split('@')[0] : nome.Trim();
            
            var novoUsuario = new Usuario
            {
                Nome = nomeFinal,
                Email = email,
                ContaId = cid,
                SenhaHash = "",
                SenhaSalt = "",
                EmailConfirmado = false,
                DeveAlterarSenha = true,
                TokenConfirmacao = token,
                CodigoConfirmacao = codigo,
                TokenExpiracao = DateTime.Now.AddHours(48),
                DataCriacao = DateTime.Now,
                Ativo = true
            };
            context.Usuarios.Add(novoUsuario);

            if (_emailService != null)
            {
                var link = !string.IsNullOrWhiteSpace(baseUrl)
                    ? $"{baseUrl.TrimEnd('/')}/definir-senha?token={token}"
                    : $"/definir-senha?token={token}";

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _emailService.EnviarEmailAtivacaoAsync(nomeFinal, email, link, codigo);
                    }
                    catch { }
                });
            }
        }

        await context.SaveChangesAsync();
        return novo;
    }

    public async Task<bool> RemoverMembroTimeAsync(int membroId, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();

        var membro = await context.MembrosTime.FirstOrDefaultAsync(m => m.Id == membroId && m.ContaId == cid);
        if (membro == null) return false;

        // Se o usuário já possuir cadastro, devolve para uma conta própria individual isolada
        var usuario = await context.Usuarios.FirstOrDefaultAsync(u => u.Email == membro.Email);
        if (usuario != null && usuario.ContaId == cid)
        {
            var novaConta = new Conta
            {
                Nome = $"Conta de {usuario.Nome}",
                DonoUsuarioId = usuario.Id,
                DataCriacao = DateTime.Now
            };
            context.Contas.Add(novaConta);
            await context.SaveChangesAsync();

            usuario.ContaId = novaConta.Id;
        }

        context.MembrosTime.Remove(membro);
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<string> ObterPapelUsuarioAtualAsync()
    {
        var usuario = await _authService.ObterUsuarioLogadoAsync();
        if (usuario == null) return "Visitante";

        var cid = await ResolverContaIdAsync(usuario.ContaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var conta = await context.Contas.FirstOrDefaultAsync(c => c.Id == cid);

        // Se o usuário logado for o Dono da Conta, seu papel é Proprietário
        if (conta != null && conta.DonoUsuarioId == usuario.Id)
        {
            return "Proprietário";
        }

        // Se estiver registrado na tabela MembrosTime da conta
        var emailNorm = usuario.Email.Trim().ToLowerInvariant();
        var membro = await context.MembrosTime.FirstOrDefaultAsync(m => m.ContaId == cid && m.Email == emailNorm && m.Ativo);
        if (membro != null && !string.IsNullOrWhiteSpace(membro.Papel))
        {
            return membro.Papel;
        }

        // Caso padrão defensivo: se a conta não tiver dono ou for conta inicial criada pelo próprio usuário
        if (conta == null || conta.DonoUsuarioId == 0 || conta.DonoUsuarioId == usuario.Id)
        {
            return "Proprietário";
        }

        return "Colaborador";
    }

    public async Task<bool> UsuarioAtualEhColaboradorAsync()
    {
        var papel = await ObterPapelUsuarioAtualAsync();
        return papel.Equals("Colaborador", StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================
    // GESTÃO DE LOJAS
    // ==========================================

    public async Task<List<Loja>> GetLojasAsync(int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas
            .AsNoTracking()
            .Where(l => l.ContaId == cid)
            .OrderBy(l => l.Nome)
            .ToListAsync();
    }

    public async Task<Loja?> GetLojaPorIdAsync(int id, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.ContaId == cid);
    }

    public async Task<Loja> CriarLojaAsync(Loja loja, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        loja.ContaId = cid;
        context.Lojas.Add(loja);
        await context.SaveChangesAsync();
        return loja;
    }

    public async Task<Loja?> AtualizarLojaAsync(int id, string nome, string codigoLoja, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FirstOrDefaultAsync(l => l.Id == id && l.ContaId == cid);
        if (loja != null)
        {
            loja.Nome = nome.Trim();
            loja.CodigoLoja = codigoLoja.Trim().ToUpperInvariant();
            await context.SaveChangesAsync();
        }
        return loja;
    }

    public async Task ExcluirLojaAsync(int id, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FirstOrDefaultAsync(l => l.Id == id && l.ContaId == cid);
        if (loja != null)
        {
            var validades = await context.RegistrosValidade.Where(r => r.LojaId == id && r.ContaId == cid).ToListAsync();
            context.RegistrosValidade.RemoveRange(validades);
            context.Lojas.Remove(loja);
            await context.SaveChangesAsync();
        }
    }

    // ==========================================
    // COLETA E PRODUTOS (CATÁLOGO POR CONTA)
    // ==========================================

    public async Task<Produto?> BuscarProdutoPorCodigoBarrasAsync(string codigoBarras, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        return await context.Produtos
            .Include(p => p.Validades.Where(v => v.Status == "Ativo" && v.ContaId == cid))
                .ThenInclude(v => v.Loja)
            .FirstOrDefaultAsync(p => p.ContaId == cid && p.CodigoBarras == codigoTratado);
    }

    public async Task<List<Produto>> BuscarProdutosPorTermoAsync(string termo, int limite = 10, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var t = (termo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t)) return new List<Produto>();

        return await context.Produtos
            .AsNoTracking()
            .Include(p => p.Validades.Where(v => v.Status == "Ativo" && v.ContaId == cid))
                .ThenInclude(v => v.Loja)
            .Where(p => p.ContaId == cid && (p.CodigoBarras.Contains(t) || p.Nome.ToLower().Contains(t.ToLower())))
            .OrderBy(p => p.CodigoBarras.StartsWith(t) ? 0 : 1)
            .ThenBy(p => p.Nome)
            .Take(limite)
            .ToListAsync();
    }

    public async Task SalvarLoteColetasAsync(IEnumerable<ItemColetaSessao> itens, int? contaId = null)
    {
        var lista = itens?.ToList() ?? new List<ItemColetaSessao>();
        if (!lista.Any()) return;

        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();

        foreach (var item in lista)
        {
            var codigoTratado = item.CodigoBarras.Trim();
            Produto? produto = null;

            if (item.ProdutoId.HasValue && item.ProdutoId.Value > 0)
            {
                produto = await context.Produtos.FirstOrDefaultAsync(p => p.ContaId == cid && p.Id == item.ProdutoId.Value);
            }

            if (produto == null)
            {
                produto = await context.Produtos.FirstOrDefaultAsync(p => p.ContaId == cid && p.CodigoBarras == codigoTratado);
            }

            if (produto == null)
            {
                produto = new Produto
                {
                    ContaId = cid,
                    CodigoBarras = codigoTratado,
                    Nome = !string.IsNullOrWhiteSpace(item.NomeProduto) ? item.NomeProduto.Trim() : "Produto Sem Nome"
                };
                context.Produtos.Add(produto);
                await context.SaveChangesAsync();
            }

            var novoRegistro = new RegistroValidade
            {
                ContaId = cid,
                LojaId = item.LojaId,
                ProdutoId = produto.Id,
                DataValidade = item.DataValidade,
                EmPromocao = item.EmPromocao,
                DataColeta = DateTime.Now,
                Status = "Ativo"
            };
            context.RegistrosValidade.Add(novoRegistro);
        }

        await context.SaveChangesAsync();
    }

    public async Task<List<RegistroValidade>> GetValidadesAtivasDoProdutoNaLojaAsync(int produtoId, int lojaId, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RegistrosValidade
            .Include(r => r.Loja)
            .Include(r => r.Produto)
            .Where(r => r.ContaId == cid && r.ProdutoId == produtoId && r.LojaId == lojaId && r.Status == "Ativo")
            .OrderBy(r => r.DataValidade)
            .ToListAsync();
    }

    public async Task<Produto> CadastrarProdutoComValidadeAsync(int lojaId, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.ContaId == cid && p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                ContaId = cid,
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
            ContaId = cid,
            ProdutoId = produto.Id,
            LojaId = lojaId,
            DataColeta = DateTime.Today,
            DataValidade = dataValidade.Date,
            EmPromocao = emPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(registro);
        await context.SaveChangesAsync();

        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado, cid))!;
    }

    public async Task<Produto> CadastrarProdutoComValidadeMultiplasLojasAsync(IEnumerable<int> lojasIds, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.ContaId == cid && p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                ContaId = cid,
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
                r.ContaId == cid &&
                r.ProdutoId == produto.Id && 
                r.LojaId == lojaId && 
                r.DataValidade == dataValidade.Date && 
                r.Status == "Ativo");

            if (!jaExiste)
            {
                context.RegistrosValidade.Add(new RegistroValidade
                {
                    ContaId = cid,
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
        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado, cid))!;
    }

    public async Task<RegistroValidade> AdicionarValidadeAoProdutoAsync(int produtoId, int lojaId, DateTime dataValidade, bool emPromocao, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var registro = new RegistroValidade
        {
            ContaId = cid,
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

    public async Task<List<RegistroValidade>> AdicionarValidadeMultiplasLojasAsync(int produtoId, IEnumerable<int> lojasIds, DateTime dataValidade, bool emPromocao, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var novosRegistros = new List<RegistroValidade>();

        foreach (var lojaId in lojasIds.Distinct())
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
                r.ContaId == cid &&
                r.ProdutoId == produtoId &&
                r.LojaId == lojaId &&
                r.DataValidade == dataValidade.Date &&
                r.Status == "Ativo");

            if (!jaExiste)
            {
                var reg = new RegistroValidade
                {
                    ContaId = cid,
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

    public async Task AtualizarNomeProdutoAsync(int produtoId, string novoNome, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var p = await context.Produtos.FirstOrDefaultAsync(prod => prod.Id == produtoId && prod.ContaId == cid);
        if (p != null && !string.IsNullOrWhiteSpace(novoNome))
        {
            p.Nome = novoNome.Trim();
            await context.SaveChangesAsync();
        }
    }

    // ==========================================
    // MONITORAMENTO
    // ==========================================

    public async Task<List<RegistroValidade>> GetValidadesAtivasAsync(int? lojaId, string filtroRapido, DateTime? dataInicio = null, DateTime? dataFim = null, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .AsNoTracking()
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.ContaId == cid && r.Status == "Ativo");

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

    public async Task AlternarPromocaoAsync(int validadeId, bool emPromocao, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FirstOrDefaultAsync(r => r.Id == validadeId && r.ContaId == cid);
        if (reg != null)
        {
            reg.EmPromocao = emPromocao;
            await context.SaveChangesAsync();
        }
    }

    public async Task DarBaixaAsync(int validadeId, string motivo, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FirstOrDefaultAsync(r => r.Id == validadeId && r.ContaId == cid);
        if (reg != null)
        {
            reg.Status = "Baixado";
            reg.DataBaixa = DateTime.Now;
            reg.MotivoBaixa = string.IsNullOrWhiteSpace(motivo) ? "Descarte/Vencido" : motivo.Trim();
            await context.SaveChangesAsync();
        }
    }

    public async Task DesfazerBaixaAsync(int validadeId, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FirstOrDefaultAsync(r => r.Id == validadeId && r.ContaId == cid);
        if (reg != null)
        {
            reg.Status = "Ativo";
            reg.DataBaixa = null;
            reg.MotivoBaixa = null;
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<RegistroValidade>> GetHistoricoBaixasAsync(int? lojaId, DateTime? dataInicio = null, DateTime? dataFim = null, string? motivo = null, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .AsNoTracking()
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.ContaId == cid && r.Status == "Baixado");

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

    public async Task<List<ItemImportacaoPlanilha>> ProcessarPreviaPlanilhaAsync(Stream stream, string nomeArquivo, int lojaDestinoId, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        var ext = Path.GetExtension(nomeArquivo)?.ToLowerInvariant();
        var rows = new List<IDictionary<string, object>>();

        if (ext == ".csv")
        {
            ms.Position = 0;
            var buffer = ms.ToArray();
            string conteudo;
            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                conteudo = utf8Strict.GetString(buffer);
            }
            catch (DecoderFallbackException)
            {
                conteudo = Encoding.Latin1.GetString(buffer);
            }

            using var reader = new StringReader(conteudo);
            var headerLine = await reader.ReadLineAsync();
            if (headerLine != null)
            {
                headerLine = headerLine.TrimStart('\uFEFF');
                char sep = headerLine.Contains(';') ? ';' : ',';
                var headers = headerLine.Split(sep).Select(h => h.Trim(' ', '"', '\r', '\n')).ToArray();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(sep).Select(p => p.Trim(' ', '"', '\r', '\n')).ToArray();
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < headers.Length && i < parts.Length; i++)
                    {
                        dict[headers[i]] = parts[i];
                    }
                    rows.Add(dict);
                }
            }
        }
        else
        {
            // Arquivo Excel (.xlsx)
            ms.Position = 0;
            try
            {
                rows = ms.Query(useHeaderRow: true, excelType: ExcelType.XLSX)
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }
            catch
            {
                ms.Position = 0;
                rows = ms.Query(useHeaderRow: true)
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }
        }

        var itens = new List<ItemImportacaoPlanilha>();
        int linhaIndex = 2;

        await using var context = await _contextFactory.CreateDbContextAsync();
        var produtosExistentes = await context.Produtos
            .Where(p => p.ContaId == cid)
            .Select(p => new { p.Id, p.CodigoBarras })
            .ToDictionaryAsync(p => p.CodigoBarras, p => p.Id);

        var validadesExistentes = await context.RegistrosValidade
            .Where(v => v.ContaId == cid && v.LojaId == lojaDestinoId && v.Status == "Ativo")
            .Select(v => new { v.ProdutoId, v.DataValidade })
            .ToListAsync();

        foreach (IDictionary<string, object> row in rows)
        {
            string? nome = null;
            string? codigo = null;
            DateTime? validade = null;
            string? lojaOrigem = null;
            string? statusOrigem = null;

            foreach (var kvp in row)
            {
                var keyNorm = NormalizarChave(CorrigirEncodingSeNecessario(kvp.Key));
                var valStr = kvp.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(valStr)) continue;

                if (nome == null && (keyNorm.Contains("nome") || keyNorm.Contains("produto") || keyNorm.Contains("descricao")))
                {
                    nome = CorrigirEncodingSeNecessario(valStr);
                }
                else if (codigo == null && (keyNorm.Contains("cod") || keyNorm.Contains("ean") || keyNorm.Contains("barr") || (keyNorm.StartsWith("c") && keyNorm.Contains("d"))))
                {
                    codigo = valStr;
                }
                else if (validade == null && (keyNorm.Contains("venc") || keyNorm.Contains("validade") || keyNorm.Contains("data")))
                {
                    validade = ConverterData(kvp.Value);
                }
                else if (lojaOrigem == null && keyNorm.Contains("loja"))
                {
                    lojaOrigem = valStr;
                }
                else if (statusOrigem == null && keyNorm.Contains("status"))
                {
                    statusOrigem = valStr;
                }
            }

            if (!string.IsNullOrWhiteSpace(codigo) || !string.IsNullOrWhiteSpace(nome))
            {
                var item = new ItemImportacaoPlanilha
                {
                    Linha = linhaIndex,
                    CodigoBarras = codigo ?? string.Empty,
                    NomeProduto = nome ?? string.Empty,
                    DataValidade = validade,
                    LojaOriginal = lojaOrigem,
                    StatusOriginal = statusOrigem
                };

                if (string.IsNullOrWhiteSpace(item.CodigoBarras))
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Código de barras ausente";
                }
                else if (string.IsNullOrWhiteSpace(item.NomeProduto))
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Nome do produto ausente";
                }
                else if (!item.DataValidade.HasValue)
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Data de validade inválida ou ausente";
                }
                else
                {
                    item.Valido = true;
                    if (produtosExistentes.TryGetValue(item.CodigoBarras, out var prodId))
                    {
                        item.ProdutoExiste = true;
                        item.ValidadeJaExisteNaLoja = validadesExistentes.Any(v => v.ProdutoId == prodId && v.DataValidade.Date == item.DataValidade.Value.Date);
                    }
                }

                itens.Add(item);
            }

            linhaIndex++;
        }

        return itens;
    }

    public async Task<ResultadoImportacao> ExecutarImportacaoAsync(int lojaDestinoId, List<ItemImportacaoPlanilha> itens, int? contaId = null)
    {
        var cid = await ResolverContaIdAsync(contaId);
        var resultado = new ResultadoImportacao
        {
            TotalLidos = itens.Count
        };

        var itensValidos = itens.Where(i => i.Valido && i.DataValidade.HasValue).ToList();
        if (!itensValidos.Any())
        {
            resultado.Sucesso = false;
            resultado.Mensagem = "Nenhum item válido para importar.";
            return resultado;
        }

        // Garante que migrações de banco pendentes (como remoção de LojaId legada em Produtos) foram executadas
        await InicializarBancoESeedAsync();

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Identifica e cria produtos no catálogo da conta em lotes (evitando limite de parâmetros do SQLite)
        var codigos = itensValidos
            .Select(i => (i.CodigoBarras ?? string.Empty).Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var produtosNoBanco = new Dictionary<string, Produto>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in codigos.Chunk(500))
        {
            var chunkList = chunk.ToList();
            var doBanco = await context.Produtos
                .Where(p => p.ContaId == cid && chunkList.Contains(p.CodigoBarras))
                .ToListAsync();

            foreach (var p in doBanco)
            {
                produtosNoBanco[p.CodigoBarras] = p;
            }
        }

        var hoje = DateTime.Today;

        foreach (var item in itensValidos)
        {
            var codLimpo = (item.CodigoBarras ?? string.Empty).Trim();
            if (codLimpo.Length > 100) codLimpo = codLimpo.Substring(0, 100);

            var nomeLimpo = (item.NomeProduto ?? string.Empty).Trim();
            if (nomeLimpo.Length > 250) nomeLimpo = nomeLimpo.Substring(0, 250);

            if (string.IsNullOrEmpty(codLimpo)) continue;

            if (!produtosNoBanco.TryGetValue(codLimpo, out var produto))
            {
                produto = new Produto
                {
                    ContaId = cid,
                    CodigoBarras = codLimpo,
                    Nome = string.IsNullOrEmpty(nomeLimpo) ? $"Produto {codLimpo}" : nomeLimpo
                };
                context.Produtos.Add(produto);
                produtosNoBanco[codLimpo] = produto;
                resultado.ProdutosCadastrados++;
            }
            else
            {
                // Se o nome no banco for genérico ou curto e a planilha trouxer um nome melhor
                if (!string.IsNullOrWhiteSpace(nomeLimpo) && nomeLimpo.Length > produto.Nome.Length)
                {
                    produto.Nome = nomeLimpo;
                    resultado.ProdutosAtualizados++;
                }
            }
        }

        await context.SaveChangesAsync();

        // Carrega validades existentes na loja de destino para evitar duplicatas ativas com mesma data
        var produtosIds = produtosNoBanco.Values.Select(p => p.Id).Distinct().ToList();
        var validadesJaAdicionadas = new HashSet<(int ProdutoId, DateTime DataValidade)>();

        foreach (var chunk in produtosIds.Chunk(500))
        {
            var chunkList = chunk.ToList();
            var validadesExistentes = await context.RegistrosValidade
                .Where(v => v.ContaId == cid && v.LojaId == lojaDestinoId && chunkList.Contains(v.ProdutoId) && v.Status == "Ativo")
                .Select(v => new { v.ProdutoId, v.DataValidade })
                .ToListAsync();

            foreach (var v in validadesExistentes)
            {
                validadesJaAdicionadas.Add((v.ProdutoId, v.DataValidade.Date));
            }
        }

        foreach (var item in itensValidos)
        {
            var codLimpo = (item.CodigoBarras ?? string.Empty).Trim();
            if (codLimpo.Length > 100) codLimpo = codLimpo.Substring(0, 100);

            if (!produtosNoBanco.TryGetValue(codLimpo, out var produto)) continue;
            var dataVal = item.DataValidade!.Value.Date;

            if (validadesJaAdicionadas.Contains((produto.Id, dataVal)))
            {
                resultado.ValidadesIgnoradasDuplicadas++;
                continue;
            }

            context.RegistrosValidade.Add(new RegistroValidade
            {
                ContaId = cid,
                ProdutoId = produto.Id,
                LojaId = lojaDestinoId,
                DataColeta = hoje, // Fixada com a data de hoje!
                DataValidade = dataVal,
                EmPromocao = false,
                Status = "Ativo"
            });

            validadesJaAdicionadas.Add((produto.Id, dataVal));
            resultado.ValidadesInseridas++;
        }

        await context.SaveChangesAsync();

        resultado.LinhasInvalidas = itens.Count(i => !i.Valido);
        resultado.Sucesso = true;
        resultado.Mensagem = $"Importação realizada com sucesso! {resultado.ValidadesInseridas} validade(s) inserida(s) e {resultado.ProdutosCadastrados} novo(s) produto(s) cadastrado(s).";

        return resultado;
    }

    private static DateTime? ConverterData(object? valor)
    {
        if (valor == null) return null;
        if (valor is DateTime dt) return dt.Date;
        if (valor is double d)
        {
            try { return DateTime.FromOADate(d).Date; } catch { }
        }
        var str = valor.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return null;

        string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(str, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resExact))
        {
            return resExact.Date;
        }
        if (DateTime.TryParse(str, new CultureInfo("pt-BR"), DateTimeStyles.None, out var resPtBr))
        {
            return resPtBr.Date;
        }
        return null;
    }

    private static readonly Encoding Win1252Encoding = ObterWindows1252();

    private static Encoding ObterWindows1252()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    private static string CorrigirEncodingSeNecessario(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        if (texto.Contains('Ã') || texto.Contains('Â') || texto.Contains('â'))
        {
            try
            {
                var bytes = Win1252Encoding.GetBytes(texto);
                var redecoded = Encoding.UTF8.GetString(bytes);
                if (!redecoded.Contains('\uFFFD'))
                {
                    return redecoded;
                }
            }
            catch
            {
                // se falhar, mantém original
            }
        }
        return texto;
    }

    private static string NormalizarChave(string? chave)
    {
        if (string.IsNullOrEmpty(chave)) return string.Empty;
        var norm = chave.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
