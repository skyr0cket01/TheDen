using System.Linq;
using Content.Shared.Targeting;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.UserInterface.Systems.Targeting.Widgets;

[GenerateTypedNameReferences]
public sealed partial class TargetingControl : UIWidget
{
    private readonly TargetingUIController _controller;
    private readonly Dictionary<TargetBodyPart, TextureButton> _bodyPartControls;

    public TargetingControl()
    {
        RobustXamlLoader.Load(this);
        _controller = UserInterfaceManager.GetUIController<TargetingUIController>();

        _bodyPartControls = new Dictionary<TargetBodyPart, TextureButton>
        {
            // TODO: ADD EYE AND MOUTH TARGETING
            { TargetBodyPart.Head, HeadButton },
            { TargetBodyPart.Torso, ChestButton },
            { TargetBodyPart.Groin, GroinButton },
            { TargetBodyPart.LeftArm, LeftArmButton },
            { TargetBodyPart.LeftHand, LeftHandButton },
            { TargetBodyPart.RightArm, RightArmButton },
            { TargetBodyPart.RightHand, RightHandButton },
            { TargetBodyPart.LeftLeg, LeftLegButton },
            { TargetBodyPart.LeftFoot, LeftFootButton },
            { TargetBodyPart.RightLeg, RightLegButton },
            { TargetBodyPart.RightFoot, RightFootButton },
        };

        foreach (var bodyPartButton in _bodyPartControls)
        {
            bodyPartButton.Value.MouseFilter = MouseFilterMode.Stop;
            bodyPartButton.Value.OnPressed += _ => SetActiveBodyPart(bodyPartButton.Key);

            TargetDoll.Texture = Theme.ResolveTexture("target_doll");
        }
    }
    private void SetActiveBodyPart(TargetBodyPart bodyPart)
    {
        _controller.CycleTarget(bodyPart);
    }

    public void SetBodyPartsVisible(TargetBodyPart bodyPart)
    {
        foreach (var bodyPartButton in _bodyPartControls)
        {
            bodyPartButton.Value.Children.First().Visible = bodyPartButton.Key == bodyPart;
        }
    }

    public void SetTargetDollVisible(bool visible)
    {
        Visible = visible;
    }
}
