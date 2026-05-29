namespace League.Clients
{
    /// <summary>
    /// 服务工厂 - 负责创建和初始化各个服务
    /// </summary>
    public class ServiceFactory
    {
        public SummonerService CreateSummonerService(LcuClient client)
        {
            return new SummonerService(client);
        }

        public MatchService CreateMatchService(LcuClient client)
        {
            return new MatchService(client);
        }

        public RankedService CreateRankedService(LcuClient client)
        {
            return new RankedService(client);
        }

        public GameflowService CreateGameflowService(LcuClient client)
        {
            return new GameflowService(client);
        }

        public ReplayService CreateReplayService(LcuClient client)
        {
            return new ReplayService(client);
        }

        public ChampionSelectService CreateChampionSelectService(LcuClient client, GameflowService gameflowService)
        {
            return new ChampionSelectService(client, gameflowService);
        }

        public ChatService CreateChatService(LcuClient client)
        {
            return new ChatService(client);
        }
    }
}