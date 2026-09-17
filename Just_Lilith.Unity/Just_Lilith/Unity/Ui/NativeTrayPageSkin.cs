using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Just_Lilith.Unity.Ui;

internal static class NativeTrayPageSkin
{
	private static readonly Color NeutralInk = new Color(0.08f, 0.08f, 0.08f, 1f);

	private static readonly Color NeutralPlaceholder = new Color(0.34f, 0.34f, 0.33f, 1f);

	private static readonly Color NeutralSelection = new Color(0.72f, 0.72f, 0.7f, 0.42f);

	internal static bool TryApply(GameObject page, Transform settingItemRoot, out string diagnostic)
	{
		diagnostic = "templates=unresolved";
		if (page == null || settingItemRoot == null || settingItemRoot.parent == null)
		{
			return false;
		}
		Transform parent = settingItemRoot.parent;
		Transform? root = FindNamed(parent, "SettingPlanelPrefabs");
		Transform transform = FindNamed(root, "Item_EditBox");
		Transform transform2 = FindNamed(root, "Item_ButtonBIg");
		if (transform == null || transform2 == null)
		{
			return false;
		}
		Image image = FindNamed(transform, "InputField (TMP)")?.GetComponent<Image>();
		Button button = FindNamed(transform, "Btn_FolderSelect")?.GetComponent<Button>();
		Button button2 = FindNamed(transform2, "ButtonBIg")?.GetComponent<Button>();
		if (image == null || button == null || button2 == null)
		{
			return false;
		}
		Image component = page.GetComponent<Image>();
		if ((object)component != null)
		{
			component.color = Color.clear;
			component.raycastTarget = false;
		}
		int num = 0;
		foreach (Button componentsInChild in page.GetComponentsInChildren<Button>(includeInactive: true))
		{
			if (!(componentsInChild == null))
			{
				CopyButtonSkin(IsCompactButton(componentsInChild.name) ? button : button2, componentsInChild);
				num++;
			}
		}
		int num2 = 0;
		foreach (InputField componentsInChild2 in page.GetComponentsInChildren<InputField>(includeInactive: true))
		{
			if (!(componentsInChild2 == null))
			{
				Image component2 = componentsInChild2.GetComponent<Image>();
				if ((object)component2 != null)
				{
					CopyImageSkin(image, component2, copyColour: true);
					componentsInChild2.transition = Selectable.Transition.ColorTint;
					num2++;
				}
			}
		}
		Image image2 = FindNamed(FindNamed(parent, "SettingPanelSpecialItem"), "panel_bg")?.GetComponent<Image>();
		int num3 = 0;
		if (image2 != null)
		{
			foreach (RectTransform componentsInChild3 in page.GetComponentsInChildren<RectTransform>(includeInactive: true))
			{
				if (!(componentsInChild3 == null) && componentsInChild3.name.StartsWith("Profile-", StringComparison.Ordinal))
				{
					Image component3 = componentsInChild3.GetComponent<Image>();
					if ((object)component3 != null)
					{
						CopyImageSkin(image2, component3, copyColour: false);
						component3.color = Neutralize(component3.color);
						num3++;
					}
				}
			}
		}
		NeutralizePageText(page);
		ApplyInputTextColours(page);
		foreach (Outline componentsInChild4 in page.GetComponentsInChildren<Outline>(includeInactive: true))
		{
			if (componentsInChild4 != null)
			{
				componentsInChild4.effectColor = Neutralize(componentsInChild4.effectColor);
			}
		}
		diagnostic = $"templates=resolved; buttons={num}; inputs={num2}; cards={num3}; edit={HierarchyPath(transform)}; big={HierarchyPath(transform2)}";
		return true;
	}

	internal static bool TryApplyChat(GameObject chatBar, Transform settingItemRoot, out string diagnostic)
	{
		diagnostic = "chat_templates=unresolved";
		if (chatBar == null || settingItemRoot == null || settingItemRoot.parent == null)
		{
			return false;
		}
		Transform? root = FindNamed(settingItemRoot.parent, "SettingPlanelPrefabs");
		Transform root2 = FindNamed(root, "Item_EditBox");
		Transform? root3 = FindNamed(root, "Item_ButtonBIg");
		Image image = FindNamed(root2, "InputField (TMP)")?.GetComponent<Image>();
		Button button = FindNamed(root3, "ButtonBIg")?.GetComponent<Button>();
		InputField inputField = FindNamed(chatBar.transform, "ChatInput")?.GetComponent<InputField>();
		Button button2 = FindNamed(chatBar.transform, "SendChat")?.GetComponent<Button>();
		if (!(image == null) && !(button == null) && !(inputField == null) && !(button2 == null))
		{
			Image component = inputField.GetComponent<Image>();
			if ((object)component != null)
			{
				Image component2 = chatBar.GetComponent<Image>();
				if ((object)component2 != null)
				{
					component2.color = Color.clear;
					component2.raycastTarget = false;
				}
				CopyImageSkin(image, component, copyColour: true);
				inputField.targetGraphic = component;
				inputField.transition = Selectable.Transition.ColorTint;
				CopyButtonSkin(button, button2);
				NeutralizePageText(chatBar);
				ApplyInputTextColours(chatBar);
				diagnostic = $"chat_templates=resolved; input={HierarchyPath(image.transform)}; send={HierarchyPath(button.transform)}";
				return true;
			}
		}
		return false;
	}

	private static bool IsCompactButton(string name)
	{
		switch (name)
		{
		case "Cancel":
		case "MoveUp":
		case "EffortUp":
		case "MoveDown":
		case "ModelUp":
		case "Refresh":
		case "ModelDown":
		case "EffortDown":
			return true;
		default:
			return false;
		}
	}

	private static void CopyButtonSkin(Button source, Button target)
	{
		target.transition = source.transition;
		target.colors = source.colors;
		ColorBlock colors = target.colors;
		colors.normalColor = Neutralize(colors.normalColor);
		colors.highlightedColor = Neutralize(colors.highlightedColor);
		colors.pressedColor = Neutralize(colors.pressedColor);
		colors.selectedColor = Neutralize(colors.selectedColor);
		colors.disabledColor = Neutralize(colors.disabledColor);
		target.colors = colors;
		target.spriteState = source.spriteState;
		target.animationTriggers = source.animationTriggers;
		if (source.targetGraphic is Image source2 && target.targetGraphic is Image target2)
		{
			CopyImageSkin(source2, target2, copyColour: true);
		}
		Color color = NeutralizeText(FindCaptionColour(source.transform));
		foreach (Text componentsInChild in target.GetComponentsInChildren<Text>(includeInactive: true))
		{
			if (componentsInChild != null)
			{
				componentsInChild.color = color;
			}
		}
	}

	private static Color FindCaptionColour(Transform source)
	{
		foreach (Graphic componentsInChild in source.GetComponentsInChildren<Graphic>(includeInactive: true))
		{
			if (!(componentsInChild == null) && !(componentsInChild is Image))
			{
				return componentsInChild.color;
			}
		}
		return new Color(0.08f, 0.08f, 0.08f, 1f);
	}

	private static void CopyImageSkin(Image source, Image target, bool copyColour)
	{
		target.sprite = source.sprite;
		target.overrideSprite = source.overrideSprite;
		target.type = source.type;
		target.preserveAspect = source.preserveAspect;
		target.fillCenter = source.fillCenter;
		target.fillMethod = source.fillMethod;
		target.fillOrigin = source.fillOrigin;
		target.fillAmount = source.fillAmount;
		target.fillClockwise = source.fillClockwise;
		target.material = source.material;
		target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
		if (copyColour)
		{
			target.color = Neutralize(source.color);
		}
	}

	private static void NeutralizePageText(GameObject page)
	{
		foreach (Text componentsInChild in page.GetComponentsInChildren<Text>(includeInactive: true))
		{
			if (!(componentsInChild == null))
			{
				componentsInChild.color = NeutralizeText(componentsInChild.color);
			}
		}
	}

	private static void ApplyInputTextColours(GameObject page)
	{
		foreach (InputField componentsInChild in page.GetComponentsInChildren<InputField>(includeInactive: true))
		{
			if (!(componentsInChild == null))
			{
				componentsInChild.caretColor = NeutralInk;
				componentsInChild.selectionColor = NeutralSelection;
				if (componentsInChild.textComponent != null)
				{
					componentsInChild.textComponent.color = NeutralInk;
				}
				if (componentsInChild.placeholder is Text text)
				{
					text.color = NeutralPlaceholder;
				}
			}
		}
	}

	private static Color Neutralize(Color colour)
	{
		float num = Mathf.Clamp01(colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f);
		return new Color(num, num, num, colour.a);
	}

	private static Color NeutralizeText(Color colour)
	{
		float num = Mathf.Clamp(Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b)), 0.58f, 0.96f);
		return new Color(num, num, num, colour.a);
	}

	private static Transform? FindNamed(Transform? root, string name)
	{
		if (root == null)
		{
			return null;
		}
		if (string.Equals(root.name, name, StringComparison.Ordinal))
		{
			return root;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform transform = FindNamed(root.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	private static string HierarchyPath(Transform transform)
	{
		Stack<string> stack = new Stack<string>();
		Transform transform2 = transform;
		while (transform2 != null)
		{
			stack.Push(transform2.name);
			transform2 = transform2.parent;
		}
		return string.Join("/", stack);
	}
}
