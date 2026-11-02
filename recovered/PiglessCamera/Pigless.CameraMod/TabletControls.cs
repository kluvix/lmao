namespace Pigless.CameraMod;

internal static class TabletControls
{
	internal static readonly TabletAction[] MainActions = new TabletAction[11]
	{
		TabletAction.Flip,
		TabletAction.Photo,
		TabletAction.Clip,
		TabletAction.Desktop,
		TabletAction.Settings,
		TabletAction.FirstPerson,
		TabletAction.ThirdPerson,
		TabletAction.GoPro,
		TabletAction.Follow,
		TabletAction.Track,
		TabletAction.Lock
	};

	internal static readonly TabletAction[] ModeActions = new TabletAction[6]
	{
		TabletAction.FirstPerson,
		TabletAction.ThirdPerson,
		TabletAction.GoPro,
		TabletAction.Follow,
		TabletAction.Track,
		TabletAction.Lock
	};

	internal static readonly TabletAction[] BasicsActions = new TabletAction[4]
	{
		TabletAction.ZoomOut,
		TabletAction.ZoomIn,
		TabletAction.Smoothing,
		TabletAction.RollLock
	};

	internal static readonly TabletAction[] DisplayActions = new TabletAction[3]
	{
		TabletAction.Quality,
		TabletAction.Mirror,
		TabletAction.Size
	};

	internal static readonly TabletAction[] SettingsCommonActions = new TabletAction[3]
	{
		TabletAction.SettingsBasics,
		TabletAction.SettingsDisplay,
		TabletAction.Done
	};

	internal static readonly TabletAction[] SettingsActions = new TabletAction[10]
	{
		TabletAction.ZoomOut,
		TabletAction.ZoomIn,
		TabletAction.Smoothing,
		TabletAction.RollLock,
		TabletAction.Quality,
		TabletAction.Mirror,
		TabletAction.Size,
		TabletAction.SettingsBasics,
		TabletAction.SettingsDisplay,
		TabletAction.Done
	};

	internal static readonly TabletRect SettingsBounds = new TabletRect(0f, 0.026f, 0.5f, 0.29f);

	internal static TabletAction[] PageActions(int page)
	{
		if (page < 1)
		{
			return BasicsActions;
		}
		return DisplayActions;
	}

	internal static TabletRect GetRect(TabletAction action)
	{
		return action switch
		{
			TabletAction.Flip => new TabletRect(-0.25f, 0.197f, 0.06f, 0.045f), 
			TabletAction.Photo => new TabletRect(-0.18f, 0.197f, 0.06f, 0.045f), 
			TabletAction.Clip => new TabletRect(0f, 0.197f, 0.1f, 0.045f), 
			TabletAction.Desktop => new TabletRect(0.18f, 0.197f, 0.06f, 0.045f), 
			TabletAction.Settings => new TabletRect(0.25f, 0.197f, 0.06f, 0.045f), 
			TabletAction.FirstPerson => ModeRect(-0.25f), 
			TabletAction.ThirdPerson => ModeRect(-0.15f), 
			TabletAction.GoPro => ModeRect(-0.05f), 
			TabletAction.Follow => ModeRect(0.05f), 
			TabletAction.Track => ModeRect(0.15f), 
			TabletAction.Lock => ModeRect(0.25f), 
			TabletAction.SettingsBasics => new TabletRect(-0.105f, 0.105f, 0.18f, 0.035f), 
			TabletAction.SettingsDisplay => new TabletRect(0.105f, 0.105f, 0.18f, 0.035f), 
			TabletAction.Done => new TabletRect(0.205f, 0.145f, 0.07f, 0.03f), 
			TabletAction.ZoomOut => new TabletRect(0.035f, 0.05f, 0.055f, 0.045f), 
			TabletAction.ZoomIn => new TabletRect(0.195f, 0.05f, 0.055f, 0.045f), 
			TabletAction.Smoothing => ValueRect(-0.012f), 
			TabletAction.RollLock => ValueRect(-0.074f), 
			TabletAction.Quality => ValueRect(0.05f), 
			TabletAction.Mirror => ValueRect(-0.012f), 
			TabletAction.Size => ValueRect(-0.074f), 
			_ => default(TabletRect), 
		};
	}

	internal static TabletAction HitTest(float x, float y, bool settingsOpen, int settingsPage = 0)
	{
		TabletAction[] settingsCommonActions;
		if (settingsOpen && SettingsBounds.Contains(x, y))
		{
			settingsCommonActions = SettingsCommonActions;
			foreach (TabletAction tabletAction in settingsCommonActions)
			{
				if (GetRect(tabletAction).Contains(x, y))
				{
					return tabletAction;
				}
			}
			settingsCommonActions = PageActions(settingsPage);
			foreach (TabletAction tabletAction2 in settingsCommonActions)
			{
				if (GetRect(tabletAction2).Contains(x, y))
				{
					return tabletAction2;
				}
			}
			return TabletAction.None;
		}
		settingsCommonActions = MainActions;
		foreach (TabletAction tabletAction3 in settingsCommonActions)
		{
			if (GetRect(tabletAction3).Contains(x, y))
			{
				return tabletAction3;
			}
		}
		return TabletAction.None;
	}

	private static TabletRect ModeRect(float x)
	{
		return new TabletRect(x, -0.178f, 0.082f, 0.068f);
	}

	private static TabletRect ValueRect(float y)
	{
		return new TabletRect(0.11f, y, 0.225f, 0.045f);
	}
}
