#if canImport(SwiftUI)
  import ColdframeDesignTokens
  import SwiftUI

  public enum ButtonVariant: Sendable {
    case primary
    case secondary
    case ghost
  }

  /// DS Button (UX-DR34): square, at least `button-height`, label naming the result, working
  /// label in place, never a spinner. The label wraps and the button grows (UX-DR126).
  public struct PrimaryButton: View {
    let label: L10n
    let variant: ButtonVariant
    let isEnabled: Bool
    let action: () -> Void
    @Environment(\.palette) private var palette

    public init(
      _ label: L10n, variant: ButtonVariant = .primary, isEnabled: Bool = true,
      action: @escaping () -> Void
    ) {
      self.label = label
      self.variant = variant
      self.isEnabled = isEnabled
      self.action = action
    }

    public var body: some View {
      Button(action: action) {
        label.text
          .role(Typography.button)
          .multilineTextAlignment(.leading)
          .fixedSize(horizontal: false, vertical: true)
          .frame(maxWidth: variant == .ghost ? nil : .infinity, alignment: .leading)
          .padding(.horizontal, Spacing.step5)
          .padding(.vertical, Spacing.step4)
          .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
          .foregroundStyle(ink)
          .background(fill)
          .contentShape(Rectangle())
      }
      .buttonStyle(.plain)
      .disabled(!isEnabled)
    }

    private var fill: Color {
      switch variant {
      case .primary: palette.primary
      case .secondary: palette.buttonSecondary
      case .ghost: .clear
      }
    }

    private var ink: Color {
      switch variant {
      case .primary: palette.inkOnBright
      case .secondary: palette.textOnColor
      case .ghost: palette.primaryText
      }
    }
  }

  /// Inline notice (UX-DR56): `layer-01`, 3 pt `border-strong` left edge, `body` text, one
  /// action or none, never dismissable. Announced when it appears (UX-DR104).
  public struct InlineNotice: View {
    let message: L10n
    /// Fills the entry's `%@`, such as the Site or Lot the notice names.
    let subject: String?
    let action: (label: L10n, perform: () -> Void)?
    let announcement: Announcement
    @Environment(\.palette) private var palette

    public init(
      message: L10n, subject: String? = nil, announcement: Announcement = .polite,
      action: (label: L10n, perform: () -> Void)? = nil
    ) {
      self.message = message
      self.subject = subject
      self.announcement = announcement
      self.action = action
    }

    private var resolved: String { subject.map { message.string($0) } ?? message.string }

    public var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Text(verbatim: resolved)
          .role(Typography.body)
          .foregroundStyle(palette.textPrimary)
          .fixedSize(horizontal: false, vertical: true)
        if let action {
          PrimaryButton(action.label, variant: .ghost, action: action.perform)
            .padding(.leading, -Spacing.step5)
        }
      }
      .padding(.vertical, Spacing.step4)
      .padding(.horizontal, Spacing.step5)
      .frame(maxWidth: .infinity, alignment: .leading)
      .background(palette.layer01)
      .overlay(alignment: .leading) { Rectangle().fill(palette.borderStrong).frame(width: 3) }
      .accessibilityElement(children: .contain)
      .onAppear { announce() }
      .onChange(of: resolved) { announce() }
    }

    private func announce() {
      var text = AttributedString(resolved)
      if announcement == .assertive {
        text.accessibilitySpeechAnnouncementPriority = .high
      }
      AccessibilityNotification.Announcement(text).post()
    }
  }

  /// DS TextInput (UX-DR35): label above, helper below; an error replaces the helper, draws the
  /// 2 pt `support-error` border with `error--filled`, and is read with the field. A password
  /// field reveals with the Carbon `view` icon.
  public struct TextInputField: View {
    let label: String
    @Binding var value: String
    let helper: String?
    let error: String?
    let isPassword: Bool
    @State private var revealed = false
    @Environment(\.palette) private var palette

    public init(
      label: String, value: Binding<String>, helper: String? = nil, error: String? = nil,
      isPassword: Bool = false
    ) {
      self.label = label
      _value = value
      self.helper = helper
      self.error = error
      self.isPassword = isPassword
    }

    public var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        Text(label).role(Typography.body).foregroundStyle(palette.textSecondary)
        HStack(spacing: Spacing.step3) {
          Group {
            if isPassword && !revealed {
              SecureField(label, text: $value)
            } else {
              TextField(label, text: $value)
            }
          }
          .role(Typography.bodyLg)
          .foregroundStyle(palette.textPrimary)
          .accessibilityHint(error ?? helper ?? "")
          if error != nil {
            CarbonIconShape(.errorFilled).fill(palette.supportErrorText).frame(
              width: 16, height: 16
            )
            .accessibilityHidden(true)
          }
          if isPassword {
            Button {
              revealed.toggle()
            } label: {
              CarbonIconShape(.view).fill(palette.textPrimary).frame(width: 20, height: 20)
                .frame(minWidth: TouchTarget.control, minHeight: TouchTarget.control)
            }
            .buttonStyle(.plain)
            .accessibilityLabel((revealed ? L10n.fieldHidePassword : .fieldShowPassword).text)
          }
        }
        .padding(.leading, Spacing.step5)
        .frame(minHeight: TouchTarget.control)
        .background(palette.field01)
        .overlay(alignment: .bottom) {
          if error == nil { Rectangle().fill(palette.borderStrong).frame(height: 1) }
        }
        .overlay {
          if error != nil { Rectangle().strokeBorder(palette.supportError, lineWidth: 2) }
        }
        if let below = error ?? helper {
          Text(below).role(Typography.helper)
            .foregroundStyle(error == nil ? palette.textHelper : palette.supportErrorText)
        }
      }
    }
  }

  /// Segmented choice (UX-DR36): equal-width square segments; the selected one is orange with a
  /// checkmark and the selected trait. Wraps to one per row when the labels do not fit.
  public struct SegmentedChoice<Value: Hashable & Sendable>: View {
    let label: L10n
    let segments: [SegmentPresentation<Value>]
    let helper: L10n?
    let onSelect: (Value) -> Void
    @Environment(\.palette) private var palette

    public init(
      label: L10n, segments: [SegmentPresentation<Value>], helper: L10n? = nil,
      onSelect: @escaping (Value) -> Void
    ) {
      self.label = label
      self.segments = segments
      self.helper = helper
      self.onSelect = onSelect
    }

    public var body: some View {
      VStack(alignment: .leading, spacing: Spacing.step3) {
        label.text.role(Typography.body).foregroundStyle(palette.textSecondary)
          .accessibilityAddTraits(.isHeader)
        EqualWidthWrap(spacing: Spacing.step1) { buttons }
        if let helper {
          helper.text.role(Typography.helper).foregroundStyle(palette.textHelper)
        }
      }
    }

    private var buttons: some View {
      ForEach(segments, id: \.value) { segment in
        Button {
          if !segment.isSelected { onSelect(segment.value) }
        } label: {
          HStack(spacing: Spacing.step3) {
            if segment.showsCheckmark {
              CarbonIconShape(.checkmark).fill(ink(segment)).frame(width: 16, height: 16)
                .accessibilityHidden(true)
            }
            segment.label.text.role(Typography.button).foregroundStyle(ink(segment))
          }
          .padding(Spacing.step4)
          .frame(maxWidth: .infinity, minHeight: TouchTarget.control)
          .background(segment.isSelected ? palette.primary : palette.layer01)
          .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(segment.traits.contains(.selected) ? .isSelected : [])
      }
    }

    private func ink(_ segment: SegmentPresentation<Value>) -> Color {
      segment.isSelected ? palette.inkOnBright : palette.textPrimary
    }
  }

  /// Children side by side at equal width when they all fit, otherwise one per row at full
  /// width, so large text wraps instead of clipping (UX-DR126).
  struct EqualWidthWrap: Layout {
    let spacing: Double

    private func fitsInRow(_ widest: Double, count: Int, width: Double) -> Bool {
      widest * Double(count) + spacing * Double(max(0, count - 1)) <= width
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
      let width = proposal.width ?? .infinity
      let ideal = subviews.map { $0.sizeThatFits(.unspecified) }
      let widest = ideal.map(\.width).max() ?? 0
      if fitsInRow(widest, count: subviews.count, width: width) {
        let column =
          width.isFinite
          ? (width - spacing * Double(max(0, subviews.count - 1))) / Double(max(1, subviews.count))
          : widest
        let height =
          subviews.map { $0.sizeThatFits(ProposedViewSize(width: column, height: nil)).height }
          .max() ?? 0
        return CGSize(
          width: width.isFinite ? width : column * Double(subviews.count), height: height)
      }
      let heights = subviews.map {
        $0.sizeThatFits(ProposedViewSize(width: width, height: nil)).height
      }
      return CGSize(
        width: width,
        height: heights.reduce(0, +) + spacing * Double(max(0, subviews.count - 1)))
    }

    func placeSubviews(
      in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()
    ) {
      let widest = subviews.map { $0.sizeThatFits(.unspecified).width }.max() ?? 0
      if fitsInRow(widest, count: subviews.count, width: bounds.width) {
        let column =
          (bounds.width - spacing * Double(max(0, subviews.count - 1)))
          / Double(max(1, subviews.count))
        for (index, subview) in subviews.enumerated() {
          subview.place(
            at: CGPoint(x: bounds.minX + Double(index) * (column + spacing), y: bounds.minY),
            proposal: ProposedViewSize(width: column, height: bounds.height))
        }
      } else {
        var y = bounds.minY
        for subview in subviews {
          let height = subview.sizeThatFits(ProposedViewSize(width: bounds.width, height: nil))
            .height
          subview.place(
            at: CGPoint(x: bounds.minX, y: y),
            proposal: ProposedViewSize(width: bounds.width, height: height))
          y += height + spacing
        }
      }
    }
  }

  /// Theme switcher (UX-DR53): System / Light / Dark, applied at once.
  public struct ThemeSwitcher: View {
    let selected: ThemePreference
    let onSelect: (ThemePreference) -> Void

    public init(selected: ThemePreference, onSelect: @escaping (ThemePreference) -> Void) {
      self.selected = selected
      self.onSelect = onSelect
    }

    public var body: some View {
      SegmentedChoice(
        label: .appearanceTheme, segments: themeSegments(selected: selected),
        helper: .appearanceThemeHelper, onSelect: onSelect)
    }
  }
#endif
