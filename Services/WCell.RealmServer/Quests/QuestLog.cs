/*************************************************************************
 *
 *   file		: QuestLog.cs
 *   copyright		: (C) The WCell Team
 *   email		: info@wcell.org
 *   last changed	: $LastChangedDate: 2008-04-08 11:02:58 +0200 (�t, 08 IV 2008) $
 *   last author	: $LastChangedBy: domiii $
 *   revision		: $Rev: 244 $
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 *************************************************************************/

using System;
using System.Collections.Generic;
using NHibernate.SqlCommand;
using WCell.Constants.GameObjects;
using WCell.Constants.Items;
using WCell.Constants.Quests;
using WCell.Constants.Updates;
using WCell.Core;
using WCell.RealmServer.Database;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Handlers;
using WCell.Constants.NPCs;
using NLog;
using WCell.Util.Threading;

namespace WCell.RealmServer.Quests
{
	/// <summary>
	/// TODO: Change dictionary to array
	/// </summary>
	public class QuestLog
	{
		public const int INVALID_SLOT = -1;

		public const int MaxQuestCount = 25;

		public const int MaxDailyQuestCount = 25;

		private static Logger log = LogManager.GetCurrentClassLogger();

		public uint[] DailyQuests;

		private readonly Character m_Owner;

		private Quest[] m_ActiveQuests;

		internal HashSet<uint> m_FinishedQuests;

		private Quest m_timedQuest;

		//private SimpleTimer m_questTimer;

		private Quest m_escortQuest;

		private List<QuestTemplate> m_DailyQuestsToday;

		private int m_activeQuestCount;

		internal List<Quest> m_RequireGOsQuests = new List<Quest>();
		internal List<Quest> m_NPCInteractionQuests = new List<Quest>();
		internal List<Quest> m_RequireItemsQuests = new List<Quest>();

		/// <summary>
		/// Initializes a new instance of the <see cref="QuestLog"/> class.
		/// </summary>
		/// <param name="owner">The owner.</param>
		public QuestLog(Character owner)
		{
			m_timedQuest = null;
			m_escortQuest = null;
			//m_questTimer = null;
			m_Owner = owner;

			m_ActiveQuests = new Quest[MaxQuestCount];
			m_DailyQuestsToday = new List<QuestTemplate>();
			//TODO: load from questlog, and it has to be loaded from update fields
			//m_CurrentDailyCount = 0;
			m_FinishedQuests = new HashSet<uint>();
		}

		#region Properties
		/// <summary>
		/// Gets the timed quest.
		/// Every Character is only allowed to solve one timed Quest at a time (makes sense doesn't it?)
		/// </summary>
		public Quest TimedQuestSlot
		{
			get { return m_timedQuest; }
		}

		/// <summary>
		/// Gets the escort quest.
		/// </summary>
		/// <value>The escort quest.</value>
		public Quest EscortQuestSlot
		{
			get { return m_escortQuest; }
		}

		/// <summary>
		/// Gets the current quests.
		/// </summary>
		/// <value>The current quests.</value>
		public Quest[] ActiveQuests
		{
			get { return m_ActiveQuests; }
		}

		public int ActiveQuestCount
		{
			get { return m_activeQuestCount; }
		}

		/// <summary>
		/// Gets the current daily count.
		/// </summary>
		/// <value>The current daily count.</value>
		public uint CurrentDailyCount
		{
			//maybe needs a fixup
			get { return (uint)m_DailyQuestsToday.Count; }
		}

		/// <summary>
		/// Gets the finished quests.
		/// </summary>
		/// <value>The finished quests.</value>
		public HashSet<uint> FinishedQuests
		{
			get { return m_FinishedQuests; }
		}

		/// <summary>
		/// Gets the owner.
		/// </summary>
		/// <value>The owner.</value>
		public Character Owner
		{
			get { return m_Owner; }
		}


		/// <summary>
		/// Determines whether the amount of active Quests is less than the maximum amount of Quests.
		/// </summary>
		public bool HasFreeSpace
		{
			get
			{
				return m_activeQuestCount < MaxQuestCount;
			}
		}

		public bool CanAcceptDailyQuest
		{
			get
			{
				return m_DailyQuestsToday.Count < MaxDailyQuestCount;
			}
		}

		public List<Quest> RequireGOQuests
		{
			get { return m_RequireGOsQuests; }
		}

		public List<Quest> RequireNPCInteractionQuests
		{
			get { return m_NPCInteractionQuests; }
		}

		public List<Quest> RequireItemQuests
		{
			get { return m_RequireItemsQuests; }
		}
		#endregion

		#region Add/Remove

		public Quest TryAddQuest(QuestTemplate template, IQuestHolder questGiver)
		{
			var slot = m_Owner.QuestLog.FindFreeSlot();
			if (slot == INVALID_SLOT)
			{
				QuestHandler.SendQuestLogFull(m_Owner);
			}
			else
			{
				var err = template.CheckBasicRequirements(m_Owner);
				if (err != QuestInvalidReason.Ok)
				{
					QuestHandler.SendQuestInvalid(m_Owner, err);
				}
				else if (m_Owner.QuestLog.GetActiveQuest(template.Id) != null)
				{
					QuestHandler.SendQuestInvalid(m_Owner, QuestInvalidReason.AlreadyHave);
				}
				else if (!template.Repeatable && m_Owner.QuestLog.FinishedQuests.Contains(template.Id))
				{
					QuestHandler.SendQuestInvalid(m_Owner, QuestInvalidReason.AlreadyCompleted);
				}
				else if (!questGiver.CanGiveQuestTo(m_Owner))
				{
					// cheat protection
					QuestHandler.SendQuestInvalid(m_Owner, QuestInvalidReason.Tired);
				}
				else
				{
					var quest = m_Owner.QuestLog.AddQuest(template, slot);
					if (quest.Template.Flags.HasFlag(QuestFlags.Escort))
					{
						AutoComplete(quest, m_Owner);
					}
					return quest;
				}
			}
			return null;
		}

		private static void AutoComplete(Quest quest, Character chr)
		{
			quest.CompleteStatus = QuestCompleteStatus.Completed;
			QuestHandler.SendComplete(quest.Template, chr);
			QuestHandler.SendQuestGiverOfferReward(chr, quest.Template, chr);
		}

		/// <summary>
		/// Adds the given new Quest.
		/// Returns null if no free slot was available or inital Items could not be handed out.
		/// </summary>
		/// <param name="qt">The qt.</param>
		/// <returns>false if it failed to add the quest</returns>
		public Quest AddQuest(QuestTemplate qt)
		{
			var slot = FindFreeSlot();
			if (slot != INVALID_SLOT)
			{
				return AddQuest(qt, slot);
			}
			return null;
		}

		/// <summary>
		/// Adds the given Quest as new active Quest
		/// Returns null if inital Items could not be handed out.
		/// </summary>
		/// <param name="qt"></param>
		/// <param name="slot"></param>
		/// <returns></returns>
		public Quest AddQuest(QuestTemplate qt, int slot)
		{
			var quest = new Quest(this, qt, slot);

			AddQuest(quest);

			return quest;
		}

		public Quest AddQuest(Quest quest)
		{
			var qt = quest.Template;

			if (!quest.IsSaved)
			{
				if (!qt.GiveInitialItems(m_Owner))
				{
					// could not add all required items
					QuestHandler.SendQuestInvalid(m_Owner, QuestInvalidReason.NoRequiredItems);
					return null;
				}
			}

			if ((qt.TimeLimit > 0) && m_timedQuest == null)
			{
				m_timedQuest = quest;
				//m_questTimer = new SimpleTimer(qt.TimeLimit);
			}
			Owner.SetQuestId((byte)quest.Slot, quest.Template.Id);
			m_ActiveQuests[quest.Slot] = quest;
			m_activeQuestCount++;

			if (qt.HasGOEvent)
			{
				m_RequireGOsQuests.Add(quest);
			}
			if (qt.HasNPCInteractionEvent)
			{
				m_NPCInteractionQuests.Add(quest);
			}

			if (quest.CollectedItems != null)
			{
				m_RequireItemsQuests.Add(quest);
				for (var i = 0; i < quest.Template.CollectableItems.Length; i++)
				{
					var item = quest.Template.CollectableItems[i];
					if (item.ItemId != ItemId.None)
					{
						// find items that are already there
						quest.CollectedItems[i] = m_Owner.Inventory.GetAmount(item.ItemId);
					}
				}
			}

			quest.UpdateStatus();

			qt.NotifyStarted(quest);
			return quest;
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="id"></param>
		/// <returns>Whether any Quest was cancelled</returns>
		public bool Cancel(uint id)
		{
			var quest = GetActiveQuest(id);
			if (quest != null)
			{
				quest.Cancel(true);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Removes the given quest.
		/// Internal - Use <see cref="Quest.Cancel"/> instead.
		/// </summary>
		internal bool RemoveQuest(uint questId)
		{
			for (var i = 0; i < m_ActiveQuests.Length; i++)
			{
				if (m_ActiveQuests[i] != null && m_ActiveQuests[i].Template.Id == questId)
				{
					RemoveQuest(m_ActiveQuests[i]);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Removes the given quest.
		/// Internal - Use <see cref="Quest.Cancel"/> instead.
		/// </summary>
		internal void RemoveQuest(Quest quest)
		{
			if (m_timedQuest == quest)
			{
				// TODO: Timed quest logic
				m_timedQuest = null;
			}

			if (quest.UsedGOs != null)
			{
				m_RequireGOsQuests.Remove(quest);
			}
			if (quest.KilledNPCs != null)
			{
				m_NPCInteractionQuests.Remove(quest);
			}

			if (quest.CollectedItems != null)
			{
				m_RequireItemsQuests.Remove(quest);
				var amt = quest.Template.CollectableItems.Length;
				for (var i = 0; i < amt; i++)
				{
					// remove all collected Items
					m_Owner.Inventory.Consume(true, (uint)quest.Template.CollectableItems[i].ItemId, quest.CollectedItems[i]);
				}
			}

			if (quest.Template.InitialItems.Count > 0)
			{
				var amt = quest.Template.InitialItems.Count;
				for (var i = 0; i < amt; i++)
				{
					// remove all initial Items
					m_Owner.Inventory.Consume(true, (uint)quest.Template.InitialItems[i].ItemId, quest.Template.InitialItems[i].Amount);
				}
			}

			// TODO: remove other Items that belong to this Quest
			RealmServer.Instance.AddMessage(new Message(quest.Delete));

			m_activeQuestCount--;
			m_ActiveQuests[quest.Slot] = null;
			Owner.ResetQuest(quest.Slot);
		}

		/// <summary>
		/// Adds the given Quest (if not already existing) marks it as completed 
		/// and offers the reward to the user.
		/// Does nothing and returns null if all Quest slots are currently used.
		/// </summary>
		//public Quest SetQuestFinished(QuestTemplate template)
		//{
		//    var quest = GetQuestById(template.Id);
		//    if (quest == null)
		//    {
		//        quest = AddQuest(template);
		//        if (quest == null)
		//        {
		//            return null;
		//        }
		//    }
		//    Owner.QuestHolderInfo.QuestStarts.Add(template);
		//    quest.Status = QuestStatus.Completable;
		//    quest.OfferReward(m_Owner);
		//    return quest;
		//}
		#endregion

		#region Checks/Getters

		/// <summary>
		/// Resets the daily quest count. Needs to be called in midnight (servertime) or when character logs in after the midnight
		/// </summary>
		public void ResetDailyQuests()
		{
			m_DailyQuestsToday.Clear();
		}

		//public void UpdateQuest(byte slot)
		//{

		//}

		public bool HasActiveQuest(QuestTemplate templ)
		{
			return GetActiveQuest(templ.Id) != null;
		}

		public bool HasActiveQuest(uint questId)
		{
			return GetActiveQuest(questId) != null;
		}

		public bool CanFinish(uint questId)
		{
			var quest = GetActiveQuest(questId);
			return quest != null && quest.CompleteStatus == QuestCompleteStatus.Completed;
		}

		/// <summary>
		/// Gets the quest by template.
		/// </summary>
		/// <returns></returns>
		public Quest GetActiveQuest(uint questId)
		{
			foreach (var quest in m_ActiveQuests)
			{
				if (quest != null)
				{
					if (quest.Template.Id == questId)
					{
						return quest;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the quest by slot.
		/// </summary>
		/// <param name="slot">The slot.</param>
		/// <returns>Quest with given slot</returns>
		public Quest GetQuestBySlot(byte slot)
		{
			return slot > MaxQuestCount ? null : m_ActiveQuests[slot];
		}

		/// <summary>
		/// Finds the free slot.
		/// </summary>
		/// <returns></returns>
		public int FindFreeSlot()
		{
			for (var i = 0; i < m_ActiveQuests.Length; i++)
			{
				if (m_ActiveQuests[i] == null)
				{
					return i;
				}
			}
			return INVALID_SLOT;
		}

		/// <summary>
		/// Gets the quest by id.
		/// </summary>
		/// <param name="qid">The qid.</param>
		/// <returns></returns>
		public Quest GetQuestById(uint qid)
		{
			foreach (var quest in m_ActiveQuests)
			{
				if (quest != null)
				{
					if (quest.Template.Id == qid)
					{
						return quest;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the QuestGiver with the given guid from the current Region (in case of a <see cref="WorldObject"/>) or 
		/// Inventory (in case of an <see cref="Item">Item</see>)
		/// </summary>
		/// <param name="guid"></param>
		/// <returns></returns>
		public IQuestHolder GetQuestGiver(EntityId guid)
		{
			IQuestHolder holder;
			if (guid.ObjectType != ObjectTypeId.Item)
			{
				holder = m_Owner.Region.GetObject(guid) as IQuestHolder;
			}
			else
			{
				holder = m_Owner.Inventory.GetItem(guid);
			}

			if (holder == null || holder.QuestHolderInfo == null)
			{
				return null;
			}

			return holder;
		}
		#endregion

		#region NPCs
		/// <summary>
		/// Is called when the owner of this QuestLog did
		/// the required interaction with the given NPC (usually killing)
		/// </summary>
		/// <param name="npc"></param>
		public void OnNPCInteraction(NPC npc)
		{
			foreach (var quest in m_NPCInteractionQuests)
			{
				if (quest.Template.NPCInteractions != null)
				{
					for (var i = 0; i < quest.Template.NPCInteractions.Length; i++)
					{
						var interaction = quest.Template.NPCInteractions[i];
						if (interaction.TemplateId == npc.Entry.Id)
						{
							if (quest.KilledNPCs[i] < interaction.Amount)
							{
								quest.KilledNPCs[i]++;
								m_Owner.SetQuestCount(quest.Slot, interaction.Index, (byte)quest.KilledNPCs[i]);
								QuestHandler.SendUpdateInteractionCount(quest, npc, interaction, quest.KilledNPCs[i], m_Owner);
								quest.UpdateStatus();
							}
							quest.Template.NotifyNPCInteracted(quest, npc);
						}
					}
				}
			}
		}

		/// <summary>
		/// Returns the first active Quest that requires the given NPC to be interacted with
		/// </summary>
		public Quest GetReqNPCQuest(NPCId npc)
		{
			for (var j = 0; j < m_NPCInteractionQuests.Count; j++)
			{
				var quest = m_NPCInteractionQuests[j];
				for (var i = 0; i < quest.Template.NPCInteractions.Length; i++)
				{
					var interaction = quest.Template.NPCInteractions[i];
					if (interaction.TemplateId == (uint)npc)
					{
						return quest;
					}
				}
			}
			return null;
		}
		#endregion

		#region Object Quests
		/// <summary>
		/// Is called when the owner of this QuestLog used the given GameObject
		/// </summary>
		/// <param name="go"></param>
		public void OnUse(GameObject go)
		{
			foreach (var quest in m_RequireGOsQuests)
			{
				if (quest.Template.GOInteractions != null)
				{
					for (var i = 0; i < quest.Template.GOInteractions.Length; i++)
					{
						var interaction = quest.Template.GOInteractions[i];
						if (interaction.TemplateId == go.Entry.Id)
						{
							if (quest.UsedGOs[i] < interaction.Amount)
							{
								quest.UsedGOs[i]++;
								m_Owner.SetQuestCount(quest.Slot, interaction.Index, (byte)quest.UsedGOs[i]);
								QuestHandler.SendUpdateInteractionCount(quest, go, interaction, quest.UsedGOs[i], m_Owner);
								quest.UpdateStatus();
							}
							quest.Template.NotifyGOUsed(quest, go);
						}
					}
				}
			}
		}

		/// <summary>
		/// The Quest that requires the given GO to be used
		/// </summary>
		public Quest GetReqObjectQuest(GOEntryId npc)
		{
			for (var j = 0; j < m_RequireItemsQuests.Count; j++)
			{
				var quest = m_RequireItemsQuests[j];
				for (var i = 0; i < quest.Template.CollectableItems.Length; i++)
				{
					var interaction = quest.Template.GOInteractions[i];
					if (interaction.TemplateId == (uint)npc)
					{
						return quest;
					}
				}
			}
			return null;
		}
		#endregion

		#region Item Quests
		/// <summary>
		/// Is called when the owner of this QuestLog receives or looses the given amount of Items
		/// </summary>
		/// <param name="item"></param>
		internal void OnItemAmountChanged(Item item, int diff)
		{
			for (var j = 0; j < m_RequireItemsQuests.Count; j++)
			{
				var quest = m_RequireItemsQuests[j];
				for (var i = 0; i < quest.Template.CollectableItems.Length; i++)
				{
					var requiredItem = quest.Template.CollectableItems[i];
					if (requiredItem.ItemId == item.Template.ItemId)
					{
						var amount = quest.CollectedItems[i];
						var newAmount = amount + diff;

						var needsUpdate = amount < requiredItem.Amount ||
							newAmount < requiredItem.Amount;

						quest.CollectedItems[i] = newAmount;
						if (needsUpdate)
						{
							QuestHandler.SendUpdateItems(item.Template.ItemId, diff, m_Owner);
							quest.UpdateStatus();
						}
						break;
					}
				}
			}
		}

		/// <summary>
		/// The Quest that requires the given Item
		/// </summary>
		public Quest GetReqItemQuest(ItemId item)
		{
			for (var j = 0; j < m_RequireItemsQuests.Count; j++)
			{
				var quest = m_RequireItemsQuests[j];
				for (var i = 0; i < quest.Template.CollectableItems.Length; i++)
				{
					var requiredItem = quest.Template.CollectableItems[i];
					if (requiredItem.ItemId == item)
					{
						return quest;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Whether the given Item is needed for an active Quest
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		public bool RequiresItem(ItemId item)
		{
			for (var j = 0; j < m_RequireItemsQuests.Count; j++)
			{
				var quest = m_RequireItemsQuests[j];
				for (var i = 0; i < quest.Template.CollectableItems.Length; i++)
				{
					var requiredItem = quest.Template.CollectableItems[i];
					if (requiredItem.ItemId == item)
					{
						return quest.CollectedItems[i] < requiredItem.Amount;
					}
				}
			}
			return false;
		}
		#endregion

		public void SaveQuests()
		{
			for (var i = 0; i < MaxQuestCount; i++)
			{
				if (m_ActiveQuests[i] != null)
				{
					m_ActiveQuests[i].Save();
				}
			}
		}

		/// <summary>
		/// If we want this method to be public, 
		/// it should update all Quests correctly (remove non-existant ones etc)
		/// </summary>
		internal void Load()
		{
			QuestRecord[] records;
			try
			{
				records = QuestRecord.GetQuestRecordForCharacter(Owner.EntityId.Low);
			}
			catch (Exception e)
			{
				RealmDBUtil.OnDBError(e);
				records = QuestRecord.GetQuestRecordForCharacter(Owner.EntityId.Low);
			}

			if (records != null)
			{
				for (var i = 0; i < records.Length; i++)
				{
					var record = records[i];
					var templ = QuestMgr.GetTemplate(record.QuestTemplateId);
					if (templ != null)
					{
						var quest = new Quest(this, record, templ);
						AddQuest(quest);
					}
					else
					{
						log.Error("Character {0} had Invalid Quest: {1} (Record: {2})", Owner,
							record.QuestTemplateId, record.QuestRecordId);
					}
				}
			}
		}

		/// <summary>
		/// Removes the given quest from the list of finished quests
		/// </summary>
		/// <param name="id"></param>
		/// <returns>Whether the Character had the given Quest</returns>
		public bool RemoveFinishedQuest(uint id)
		{
			if (m_FinishedQuests.Remove(id))
			{
				Owner.FindAndSendQGStatus();
				return true;
			}
			return false;
		}
	}
}
