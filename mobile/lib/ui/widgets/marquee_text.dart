import 'package:flutter/material.dart';

class MarqueeText extends StatefulWidget {
  final String text;
  final TextStyle? style;
  final Duration scrollDuration;
  final Duration pauseDuration;
  final double fadeWidth;

  const MarqueeText({
    super.key,
    required this.text,
    this.style,
    this.scrollDuration = const Duration(seconds: 6),
    this.pauseDuration = const Duration(seconds: 2),
    this.fadeWidth = 16.0,
  });

  @override
  State<MarqueeText> createState() => _MarqueeTextState();
}

class _MarqueeTextState extends State<MarqueeText> with SingleTickerProviderStateMixin {
  late ScrollController _scrollController;
  late AnimationController _animationController;
  
  bool _shouldScroll = false;
  final GlobalKey _textKey = GlobalKey();

  @override
  void initState() {
    super.initState();
    _scrollController = ScrollController();
    _animationController = AnimationController(
      vsync: this,
      duration: widget.scrollDuration,
    );

    // Check if scrolling is needed after the first frame
    WidgetsBinding.instance.addPostFrameCallback((_) => _checkScroll());
  }

  @override
  void didUpdateWidget(MarqueeText oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.text != widget.text || oldWidget.style != widget.style) {
      _animationController.stop();
      _scrollController.jumpTo(0);
      WidgetsBinding.instance.addPostFrameCallback((_) => _checkScroll());
    }
  }

  @override
  void dispose() {
    _scrollController.dispose();
    _animationController.dispose();
    super.dispose();
  }

  void _checkScroll() {
    if (!mounted) return;
    
    final RenderBox? renderBox = _textKey.currentContext?.findRenderObject() as RenderBox?;
    if (renderBox == null) return;

    final textWidth = renderBox.size.width;
    final containerWidth = context.size?.width ?? 0;

    if (textWidth > containerWidth) {
      setState(() {
        _shouldScroll = true;
      });
      _startScrolling();
    } else {
      setState(() {
        _shouldScroll = false;
      });
      _animationController.stop();
    }
  }

  void _startScrolling() async {
    if (!mounted || !_shouldScroll) return;

    await Future.delayed(widget.pauseDuration);
    if (!mounted || !_shouldScroll) return;

    if (_scrollController.hasClients) {
      final maxScroll = _scrollController.position.maxScrollExtent;
      await _scrollController.animateTo(
        maxScroll,
        duration: widget.scrollDuration,
        curve: Curves.linear,
      );
    }
    
    if (!mounted || !_shouldScroll) return;

    await Future.delayed(widget.pauseDuration);
    if (!mounted || !_shouldScroll) return;

    if (_scrollController.hasClients) {
      _scrollController.jumpTo(0);
      _startScrolling();
    }
  }

  @override
  Widget build(BuildContext context) {
    // If we don't know yet if we should scroll, we render the text to measure it
    // We wrap it in a SingleChildScrollView to allow measurement but disable user scrolling
    return ShaderMask(
      shaderCallback: (Rect bounds) {
        return LinearGradient(
          begin: Alignment.centerLeft,
          end: Alignment.centerRight,
          colors: [
            Colors.transparent,
            Colors.white,
            Colors.white,
            Colors.transparent,
          ],
          stops: [
            0.0,
            widget.fadeWidth / bounds.width,
            1.0 - (widget.fadeWidth / bounds.width),
            1.0,
          ],
        ).createShader(bounds);
      },
      blendMode: BlendMode.dstIn,
      child: SingleChildScrollView(
        controller: _scrollController,
        scrollDirection: Axis.horizontal,
        physics: const NeverScrollableScrollPhysics(),
        child: Text(
          widget.text,
          key: _textKey,
          style: widget.style,
          maxLines: 1,
        ),
      ),
    );
  }
}
