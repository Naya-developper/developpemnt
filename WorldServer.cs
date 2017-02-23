           // Code de l'abonnement il vérifie dans la db si le personnage est ou non abonné (via un achat vip par ex) si il est abonné il est peut mettre des equipement vip
           
           WorldClient[] array2 = array;
            for (int i = 0; i < array2.Length; i++)
            {
                WorldClient worldClient = array2[i];
                if (worldClient.WorldAccount.Subscription == 1)
                {
                    worldClient.WorldAccount.Subscription = 0;
                    worldClient.WorldAccount.SubscriptionTime = 0;
                    ServerBase<WorldServer>.Instance.DBAccessor.Database.Update(worldClient.WorldAccount);
                    worldClient.Character.SendServerMessage("Fin de l'abonnement vip à NOM_DE_SERVEUR", Color.Red);
                    worldClient.Character.OpenPopup("ATTENTION ! Les équipements demandant un grade vip vous ont été retiré !");
                    worldClient.Character.OpenPopup("Votre durée d'abonnement vip à NOM_DE_SERVEUR vien d'expiré");

                    foreach (var premiumItems in worldClient.Character.Inventory.GetEquipedItems().Where(x => x.Template.Criteria == "Pre=1"))
                    {
                        worldClient.Character.Inventory.MoveItem(premiumItems, CharacterInventoryPositionEnum.INVENTORY_POSITION_NOT_EQUIPED);
                    }
                }
            }
      }
            
____________________________________________________________________________________________________________________________________ 
     //    Critère dans la db d'être abonné pour pouvoir équipé un item
         
using Stump.Server.WorldServer.Game.Actors.RolePlay.Characters;
using System;
namespace Stump.Server.WorldServer.Game.Conditions.Criterions
{
	public class PremiumAccountCriterion : Criterion
	{
		public const string Identifier = "Pre";
		public bool HeIsPremium
		{
			get;
			set;
		}
        public override bool Eval(Character character)
        {
            bool result;
            if (base.Operator == ComparaisonOperatorEnum.EQUALS)
            {
                result = character.Client.WorldAccount.Subscription == 1;
                this.HeIsPremium = true;
            }
            else
            {
                result = (base.Operator != ComparaisonOperatorEnum.INEQUALS || character.Client.WorldAccount.Subscription != 1);
                this.HeIsPremium = false;
            }
            return result;
        }
		public override void Build()
		{
			int num;
			if (!int.TryParse(base.Literal, out num))
			{
				throw new System.Exception(string.Format("Cannot build PreniumAccountCriterion, {0} is not a valid prenium id", base.Literal));
			}
			this.HeIsPremium = (num == 1);
		}
		public override string ToString()
		{
			return base.FormatToString("Pre");
		}
	}
}

____________________________________________________________________________________________________________________________________

