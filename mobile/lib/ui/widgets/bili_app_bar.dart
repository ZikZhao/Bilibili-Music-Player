import 'package:flutter/material.dart';
import '../theme/app_colors.dart';

class BiliAppBar extends StatelessWidget implements PreferredSizeWidget {
  final String title;
  final List<Widget>? actions;
  final Widget? leading;
  final bool centerTitle;
  final Color? backgroundColor;

  const BiliAppBar({
    super.key,
    required this.title,
    this.actions,
    this.leading,
    this.centerTitle = false,
    this.backgroundColor,
  });

  @override
  Widget build(BuildContext context) {
    // Dynamic Text Color: Use onSurface so it works in both Light and Dark modes
    final textColor = Theme.of(context).colorScheme.onSurface;

    return AppBar(
      // Dynamic Background: Use scaffoldBackgroundColor as requested
      backgroundColor:
          backgroundColor ?? Theme.of(context).scaffoldBackgroundColor,
      elevation: 0,
      scrolledUnderElevation: 0,
      leading: leading, // AppBar automatically handles the back button
      title: Row(
        mainAxisSize: MainAxisSize.min,
        // Vertical Alignment: Strictly use center
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          // Decorative pill
          Padding(
            padding: const EdgeInsets.only(top: 2), // 微调：视觉上下沉 2px
            child: Container(
              width: 4,
              height: 20, // 微调：加高到 20
              decoration: BoxDecoration(
                color: AppColors.bilibiliPink,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(width: 8),
          // Title text
          Flexible(
            child: Text(
              title,
              style: TextStyle(
                color: textColor, // Dynamic color
                fontSize: 20,
                fontWeight: FontWeight.bold,
              ),
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
      centerTitle: centerTitle,
      actions: actions,
      iconTheme: IconThemeData(
        color: textColor, // Ensure icons adapt to theme
      ),
    );
  }

  @override
  Size get preferredSize => const Size.fromHeight(kToolbarHeight);
}
