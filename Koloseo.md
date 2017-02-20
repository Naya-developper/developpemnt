using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Game.Actors.Fight.Arena;
using Game.Fights.Arenas;
using Handlers.Context.RolePlay.Arena;
using Stump.Core.Attributes;
using Stump.Core.Collections;
using Stump.Core.Reflection;
using Stump.DofusProtocol.Enums;
using Stump.Server.WorldServer.Game.Actors.RolePlay.Characters;
using Stump.Server.WorldServer.Game.Fights;
using Stump.Server.WorldServer.Game.Parties;

namespace Stump.Server.WorldServer.Game.Fights.Arenas
{
public class Arena : Singleton
{
[Variable]
public static byte MinimumLevelArena = 50;

    public PvpArenaTypeEnum DefaultBattleMode = PvpArenaTypeEnum.ARENA_TYPE_3VS3;
    private const short FightStartDuration = 30;

    private readonly List<CharacterArenaFighter> m_subscribedCharacterArenaFighters =
        new List<CharacterArenaFighter>();

    private readonly object m_subscriptionLocker = new object();

    public event Action<CharacterArenaFighter> MemberArenaAdded;
    public event Action<CharacterArenaFighter> MemberArenaRemoved;
    public event Action<CharacterArenaFighter> MemberArenaAccepted;
    public event Action<CharacterArenaFighter> MemberArenaDeclined;
    public event Action<Fight> FightValidated;

    private readonly Random rand = new Random();

    private readonly int[] m_arenaMapsId =
    {
		94634497,
		94634499,
		94634501,
		94634507,
		94634509,
		94634511,
		94634513,
		94634515,
		94634517,
		94634519,
		94634505,
		94634503
    };

    protected virtual void OnMemberArenaAdded(CharacterArenaFighter characterArenaFighter)
    {
        characterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_REGISTRED;

        ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client, true,
            (sbyte)characterArenaFighter.PvpArenaStep, (sbyte)DefaultBattleMode);

        Action<CharacterArenaFighter> handler = MemberArenaAdded;

        if (handler != null)
            handler(characterArenaFighter);
    }

    protected virtual void OnMemberArenaRemoved(CharacterArenaFighter characterArenaFighter)
    {
        characterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_UNREGISTER;

        ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client
            , false, (sbyte)characterArenaFighter.PvpArenaStep, (sbyte)DefaultBattleMode);

        Action<CharacterArenaFighter> handler = MemberArenaRemoved;

        if (handler != null)
            handler(characterArenaFighter);
    }

    protected virtual void OnMemberArenaAccepted(CharacterArenaFighter characterFighter)
    {
        characterFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_WAITING_FIGHT;

        Action<CharacterArenaFighter> handler = MemberArenaAccepted;
        if (handler != null) handler(characterFighter);
    }

    protected virtual void OnMemberArenaDeclined(CharacterArenaFighter characterFighter)
    {
        UnSubscribeMember(characterFighter.Character);

        Action<CharacterArenaFighter> handler = MemberArenaDeclined;
        if (handler != null) handler(characterFighter);
    }

    protected virtual void OnFightValidated(Fight fight)
    {
        var fighters = m_subscribedCharacterArenaFighters.Where(entry => entry.CurrentFight == fight);

        foreach (var characterArenaFighter in fighters)
        {
            characterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_STARTING_FIGHT;

            ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client,
                false, (sbyte)characterArenaFighter.PvpArenaStep, (sbyte)DefaultBattleMode);
        }

        Action<Fight> handler = FightValidated;
        if (handler != null) handler(fight);
    }

    public void AddMember(Character character)
    {
        if (character.Level < MinimumLevelArena)
        {
            character.SendServerMessage(
                string.Format("Vous devez être <b>au moins niveau {0}</b> pour vous inscrire", MinimumLevelArena),
                Color.Red);
            return;
        }

        if (character.IsArenaBlackListed)
        {
            character.SendServerMessage(
                string.Format("vous venez de quitter un combat, Vous devez attendre <b>{0} min</b>",
                    character.BlackListedUntil.Subtract(DateTime.Now).Minutes + 1), Color.Red);
            return;
        }


        if (IsMember(character))
            return;

        if (character.IsInPartyArena())
        {
            if (character.IsPartyArenaLeader())
                AddPartyArena(character.PartyArena);
            else
            {
                character.SendServerMessage("Seul le chef du groupe peut vous inscrire");
                return;
            }
        }
        else
            AddCharacter(character);

        if (m_subscribedCharacterArenaFighters.Count >= (sbyte)DefaultBattleMode)
        {
            lock (m_subscriptionLocker)
                CheckFight();
        }
    }

    private void AddCharacter(Character character)
    {
        var arenaFighter = new CharacterArenaFighter(character);
        lock (m_subscriptionLocker)
            m_subscribedCharacterArenaFighters.Add(arenaFighter);

        OnMemberArenaAdded(arenaFighter);
    }

    public void AddPartyArena(PartyArena partyArena)
    {
        var members = partyArena.Members;

        foreach (var arenaFighter in members.Select(entry => new CharacterArenaFighter(entry)))
        {
            lock (m_subscriptionLocker)
                m_subscribedCharacterArenaFighters.Add(arenaFighter);

            OnMemberArenaAdded(arenaFighter);
        }
    }

    private void CheckFight()
    {
        if (m_subscribedCharacterArenaFighters.Count(i => i.IsDispo) < (sbyte)DefaultBattleMode * 2)
        {
            return;
        }

        List<ArenaTeam> possibleTeams = new List<ArenaTeam>();
        var teamsByCount = new Dictionary<int, List<ArenaTeam>>();

        for (int i = 1; i < (sbyte)DefaultBattleMode; i++)
        {
            teamsByCount[i] = new List<ArenaTeam>();
        }

        List<ArenaTeam> teams = FindPossibleTeamsCombination();

        if (teams == null)
            return;

        var randomMap = m_arenaMapsId[rand.Next(m_arenaMapsId.Length)];

        var fight = FightManager.Instance.CreatePvPArenaFight(World.Instance.GetMap(randomMap),
            DefaultBattleMode);

        foreach (var characterArenaFighter in teams[0].Fighters)
        {
            characterArenaFighter.Team = teams[0];
            characterArenaFighter.OpposedTeam = teams[1];
            characterArenaFighter.Character.ArenaFightId = fight.Id;
            characterArenaFighter.CurrentFight = fight;
        }

        foreach (var characterArenaFighter in teams[1].Fighters)
        {
            characterArenaFighter.Team = teams[1];
            characterArenaFighter.OpposedTeam = teams[0];
            characterArenaFighter.Character.ArenaFightId = fight.Id;
            characterArenaFighter.CurrentFight = fight;
        }

        SendPropositionTo(teams[0]);
        SendPropositionTo(teams[1]);
    }

    private List<ArenaTeam> FindPossibleTeamsCombination(bool ForceGroup = false)
    {
        List<ArenaTeam> teams = new List<ArenaTeam>() { new ArenaTeam(), new ArenaTeam() };
        int level = 0, rank = 0;
        for (int i = 0; i < 2; i++)
        {
            foreach (CharacterArenaFighter fighter in m_subscribedCharacterArenaFighters.FindAll(z => z.IsDispo && !teams[0].Fighters.Contains(z)))
            {
                if (teams[i].Fighters.Count == 0 && level + rank == 0)
                {
                    if (fighter.Character.IsInPartyArena())
                    {
                        foreach (var arenaFighter in fighter.Character.PartyArena.Members)
                        {
                            teams[i].Fighters.Add(m_subscribedCharacterArenaFighters.FirstOrDefault(x => x.Character.Id == arenaFighter.Id));
                        }
                    }
                    else
                        teams[i].Fighters.Add(fighter);
                    level = fighter.Character.Level;
                    rank = fighter.Character.Rank;
                }
                else
                {
                    if (fighter.Character.Level - level < 10 && fighter.Character.Level - level > -10 && fighter.Character.Rank - rank < 750 && fighter.Character.Rank - rank > -750)
                    {
                        if (fighter.Character.IsInPartyArena() && (fighter.Character.PartyArena.Members.Count() < (sbyte)DefaultBattleMode - teams[i].Fighters.Count || ForceGroup == true))
                        {
                            if (ForceGroup)
                                teams[i].Fighters.Clear();
                            foreach (var arenaFighter in fighter.Character.PartyArena.Members)
                            {
                                teams[i].Fighters.Add(m_subscribedCharacterArenaFighters.FirstOrDefault(x => x.Character.Id == arenaFighter.Id));
                            }
                        }
                        else if (!fighter.Character.IsInPartyArena())
                            teams[i].Fighters.Add(fighter);
                    }
                }
                if (teams[i].Fighters.Count == (sbyte)DefaultBattleMode)
                    break;
            }
        }

        if (teams[0].Fighters.Count == (sbyte)DefaultBattleMode && teams[1].Fighters.Count == (sbyte)DefaultBattleMode)
            return teams;
        else if (ForceGroup == true)
            return null;
        else
            return FindPossibleTeamsCombination(true);
    }

    private void SendPropositionTo(ArenaTeam arenaTeam)
    {
        foreach (CharacterArenaFighter arenaFighter in arenaTeam.Fighters)
        {
            var alliesId = arenaTeam.Fighters.FindAll(x => x.Character.Id != arenaFighter.Character.Id).Select(entry => entry.Character.Id).ToList();

            ArenaHandler.SendGameRolePlayArenaFightPropositionMessage(arenaFighter.Character.Client, arenaFighter.CurrentFight.Id, alliesId, FightStartDuration);
            arenaFighter.Character.InNotification = 1;
            Console.WriteLine("name : " + arenaFighter.Character.Name + " notification ");
        }
    }

    public bool RemoveMember(Character character)
    {
        var arenaFighter = m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);

        if (arenaFighter == null)
            return false;

        lock (m_subscriptionLocker)
            m_subscribedCharacterArenaFighters.Remove(arenaFighter);

        OnMemberArenaRemoved(arenaFighter);

        return true;
    }

    public bool IsMember(Character character)
    {
        return m_subscribedCharacterArenaFighters.
            Any(entry => entry.Character == character);
    }

    private ArenaTeam GetTeamArena(PartyArena partyArena)
    {
        lock (m_subscriptionLocker)
        {
            var list = partyArena.Members.Where(x => x.Id == partyArena.Leader.Id).ToList();
            foreach (var n in list)
            {
                Console.WriteLine(n.Name);
            }
            var arenaFighters = (from x in partyArena.Members
                                 select m_subscribedCharacterArenaFighters.FirstOrDefault(i => i.Character.Id == x.Id)).ToList();
            //partyArena.Members.Select(
            //    member => m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character.Id == member.Id))
            //    .ToList();

            return new ArenaTeam(arenaFighters);
        }
    }

    private ArenaTeam GetTeamArena(Character character)
    {
        CharacterArenaFighter arenaFighter;

        lock (m_subscriptionLocker)
            arenaFighter = m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);

        if (arenaFighter == null)
        {
            Console.WriteLine("Fucking bug de la mort qui tue et je l'emmerde ce bug !");
        }

        List<CharacterArenaFighter> listOfFighters = new List<CharacterArenaFighter>
        {
            arenaFighter
        };

        return new ArenaTeam(listOfFighters);
    }

    public void HandleAccepted(Character character, bool accept, int fightId)
    {
        CharacterArenaFighter characterFighter;

        lock (m_subscriptionLocker)
            characterFighter =
                m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);

        if (characterFighter == null)
            return;

        if (characterFighter.PvpArenaStep != PvpArenaStepEnum.ARENA_STEP_REGISTRED)
            return;

        if (characterFighter.CurrentFight == null)
            return;

        if (characterFighter.CurrentFight.Id != fightId)
            return;

        var fight = characterFighter.CurrentFight;

        if (character.IsFighting())
            accept = false;

        List<CharacterArenaFighter> fighters = new List<CharacterArenaFighter>();
        fighters = m_subscribedCharacterArenaFighters.FindAll(x => x.CurrentFight != null && x.CurrentFight.Id == fight.Id);

        if (accept)
        {
            foreach (var subscribedCharacterArenaFighter in fighters)
            {
                ArenaHandler.SendGameRolePlayArenaFighterStatusMessage(subscribedCharacterArenaFighter.Character.Client, fightId, character.Id, true);
            }

            characterFighter.Accepted = true;

            OnMemberArenaAccepted(characterFighter);

            if (fighters.Count(entry => entry.Accepted == true) == (sbyte)DefaultBattleMode * 2)
            {
                OnFightValidated(characterFighter.CurrentFight);

                var firstTeam = characterFighter.Team;
                var secondTeam = characterFighter.OpposedTeam;

                foreach (var characterArenaFighter in firstTeam.Fighters)
                {
                    var cell = characterArenaFighter.Character.Cell;
                    var map = characterArenaFighter.Character.Map;
                    characterArenaFighter.Character.Teleport(fight.Map, fight.Map.GetRandomFreeCell());
                    fight.RedTeam.AddFighter(characterArenaFighter.Character.CreateFighter(fight.RedTeam));
                    characterArenaFighter.Character.NextMap = map;
                    characterArenaFighter.Character.NextCell = cell;
                    characterArenaFighter.Character.InNotification = 0;
                    ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client, false, (sbyte)characterFighter.PvpArenaStep, (sbyte)DefaultBattleMode);
                }

                foreach (var characterArenaFighter in secondTeam.Fighters)
                {
                    var cell = characterArenaFighter.Character.Cell;
                    var map = characterArenaFighter.Character.Map;
                    characterArenaFighter.Character.Teleport(fight.Map, fight.Map.GetRandomFreeCell());
                    fight.BlueTeam.AddFighter(characterArenaFighter.Character.CreateFighter(fight.BlueTeam));
                    characterArenaFighter.Character.NextMap = map;
                    characterArenaFighter.Character.NextCell = cell;
                    characterArenaFighter.Character.InNotification = 0;
                    ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client, false, (sbyte)characterFighter.PvpArenaStep, (sbyte)DefaultBattleMode);
                }

                fight.StartPlacement();

                return;
            }
        }
        else
        {
            characterFighter.Accepted = false;
            OnMemberArenaDeclined(characterFighter);
            foreach (var subscribedCharacterArenaFighter in fighters)
            {
                subscribedCharacterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_REGISTRED;
                subscribedCharacterArenaFighter.Character.InNotification = 0;
                ArenaHandler.SendGameRolePlayArenaFighterStatusMessage(subscribedCharacterArenaFighter.Character.Client, fightId, character.Id, false);
            }
            FightManager.Instance.Remove(fight);
        }

        List<CharacterArenaFighter> remainingFighter = new List<CharacterArenaFighter>();

        //lock (m_subscriptionLocker)
        //    remainingFighter =
        // m_subscribedCharacterArenaFighters.Where(entry => entry.Character.InNotification == 1).Where(entry => entry.CurrentFight.Id == fightId).ToList();

        //if (remainingFighter.Any(entry => entry.Accepted == null))
        //    return;

        foreach (var subscribedCharacterArenaFighter in remainingFighter)
        {
            SetRegistered(subscribedCharacterArenaFighter.Character);
        }

    }

    public void UnSubscribeMember(Character character)
    {

        if (character.IsPartyArenaLeader())
        {
            CharacterArenaFighter fighter;

            lock (m_subscriptionLocker)
                fighter = m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);
            if (fighter == null)
                return;

            if (fighter.PvpArenaStep == PvpArenaStepEnum.ARENA_STEP_REGISTRED && fighter.CurrentFight != null)
            {
                var fight = fighter.CurrentFight;
                List<CharacterArenaFighter> fighters = new List<CharacterArenaFighter>();
                lock (m_subscriptionLocker)
                    fighters = m_subscribedCharacterArenaFighters.Where(entry => entry.CurrentFight == fight).ToList();
                foreach (
                var subscribedCharacterArenaFighter in fighters)
                {
                    subscribedCharacterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_REGISTRED;
                    subscribedCharacterArenaFighter.Character.InNotification = 0;
                    ArenaHandler.SendGameRolePlayArenaFighterStatusMessage(
                        subscribedCharacterArenaFighter.Character.Client, fight.Id, character.Id, false);
                }
            }

            foreach (var n in m_subscribedCharacterArenaFighters.Where(x => x.Character.IsInPartyArena() && x.Character.PartyArena.Id == character.PartyArena.Id).ToList())
            {
                n.CurrentFight = null;
                n.Team = null;
                n.OpposedTeam = null;
                n.Accepted = null;
                RemoveMember(n.Character);
            }

        }
        else
        {
            CharacterArenaFighter fighter;

            lock (m_subscriptionLocker)
                fighter = m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);
            if (fighter == null)
                return;

            if (fighter.PvpArenaStep == PvpArenaStepEnum.ARENA_STEP_REGISTRED && fighter.CurrentFight != null)
            {
                var fight = fighter.CurrentFight;
                List<CharacterArenaFighter> fighters = new List<CharacterArenaFighter>();
                lock (m_subscriptionLocker)
                    fighters = m_subscribedCharacterArenaFighters.Where(entry => entry.CurrentFight == fight).ToList();
                foreach (
                var subscribedCharacterArenaFighter in fighters)
                {
                    subscribedCharacterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_REGISTRED;
                    subscribedCharacterArenaFighter.Character.InNotification = 0;
                    ArenaHandler.SendGameRolePlayArenaFighterStatusMessage(
                        subscribedCharacterArenaFighter.Character.Client, fight.Id, character.Id, false);
                }
            }

            fighter.CurrentFight = null;
            fighter.Team = null;
            fighter.OpposedTeam = null;
            fighter.Accepted = null;
            RemoveMember(character);

        }
    }

    public void SetRegistered(Character character)
    {
        CharacterArenaFighter characterArenaFighter;

        lock (m_subscriptionLocker)
            characterArenaFighter = m_subscribedCharacterArenaFighters.SingleOrDefault(entry => entry.Character == character);

        if (characterArenaFighter == null)
            return;

        characterArenaFighter.PvpArenaStep = PvpArenaStepEnum.ARENA_STEP_REGISTRED;
        characterArenaFighter.CurrentFight = null;
        characterArenaFighter.Accepted = null;
        characterArenaFighter.Team = null;
        characterArenaFighter.OpposedTeam = null;

        ArenaHandler.SendGameRolePlayArenaRegistrationStatusMessage(characterArenaFighter.Character.Client, true,
            (sbyte)characterArenaFighter.PvpArenaStep, (sbyte)DefaultBattleMode);
    }

    public void PlayerLeft(Character character)
    {
        UnSubscribeMember(character);

        character.BlackListedUntil = DateTime.Now.Add(new TimeSpan(0, 0, 30, 0));
    }

    public int GetNumberSubscripted()
    {
        return m_subscribedCharacterArenaFighters.Count();
    }

    public int GetNumberFighter()
    {
        return m_subscribedCharacterArenaFighters.Count(x => x.PvpArenaStep == PvpArenaStepEnum.ARENA_STEP_STARTING_FIGHT);
    }

    public bool UnscripbleAll()
    {
        try
        {
            List<Character> list = m_subscribedCharacterArenaFighters.Select(x => x.Character).ToList();
            foreach (var character in list)
            {
                UnSubscribeMember(character);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetMode(int number)
    {
        DefaultBattleMode = (PvpArenaTypeEnum)number;
        CheckFight();
    }
}
